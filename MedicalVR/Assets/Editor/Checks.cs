using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runnable check (no test package needed):
// Unity -batchmode -nographics -projectPath ui/MedicalVR -executeMethod Checks.Run -logFile -
public static class Checks
{
    public static void Run()
    {
        int failed = 0;
        void Expect(bool ok, string what) { if (!ok) { failed++; Debug.LogError("CHECK FAIL " + what); } else Debug.Log("CHECK ok " + what); }

        // WAV round trip, PCM16 mono 24 kHz as ai/CONTRACT.md uses.
        var samples = new float[2400];
        for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * 0.1f) * 0.5f;
        var clip = Wav.ToClip(Wav.FromSamples(samples, 24000));
        var back = new float[clip.samples];
        clip.GetData(back, 0);
        Expect(clip.frequency == 24000 && clip.channels == 1 && back.Length == samples.Length, "wav header");
        Expect(Mathf.Abs(back[123] - samples[123]) < 0.001f, "wav samples");

        // Parse the turn and feedback examples from ai/CONTRACT.md (snake_case keys).
        var turn = JsonUtility.FromJson<TurnReply>("{\"student_text\":\"Where is the pain?\",\"patient_text\":\"Right in the middle.\",\"emotion\":\"anxious\",\"gesture\":\"point_chest\",\"audio_base64\":null,\"audio_format\":\"wav\",\"fallback\":false}");
        Expect(turn.patient_text == "Right in the middle." && turn.emotion == "anxious", "turn json");
        var fb = JsonUtility.FromJson<Feedback>("{\"overall_percent\":15,\"items\":[{\"id\":\"intro_self\",\"text\":\"Introduces self\",\"achieved\":true,\"evidence\":\"Hello, I'm Anna.\",\"at_s\":0}],\"suggestions\":[\"x\"]}");
        Expect(fb.overall_percent == 15 && fb.items[0].achieved && fb.items[0].evidence == "Hello, I'm Anna.", "feedback json");

        var fb2 = JsonUtility.FromJson<Feedback>("{\"overall_percent\":40,\"rubric_percent\":38,\"graded_against\":\"reference\",\"items\":[{\"id\":\"a\",\"text\":\"Asks about skin\",\"achieved\":false,\"in_reference\":true,\"reference_line\":\"Have you noticed any change in your skin?\"}]}");
        Expect(fb2.graded_against == "reference" && fb2.rubric_percent == 38 && fb2.items[0].in_reference && fb2.items[0].reference_line.StartsWith("Have you"), "feedback reference fields");

        // Mock client runs the whole loop. Task.Run keeps continuations off Unity's main thread,
        // which .Result would otherwise block (deadlock).
        IAIClient ai = new MockAIClient();
        var s = Task.Run(() => ai.StartSession("mock_case")).Result;
        var r = Task.Run(() => ai.Turn(s.session_id, "Hello", null)).Result;
        Expect(r.patient_text.Contains("Hello"), "mock turn");
        Expect(Task.Run(() => ai.End(s.session_id)).Result.items.Length == 2, "mock end");

        // Hannah's clips must be legacy: the headset cannot sample other clips without an Animator (she stood in T-pose).
        int n = 0;
        foreach (var id in App.Characters.Select(c => c.id).Append(App.Doctor)) {
            var prefab = Resources.Load<GameObject>(id);
            if (prefab == null) continue;
            var so = new SerializedObject(prefab.GetComponent<AvatarPatient>());
            var sit = so.FindProperty("sitClip").objectReferenceValue as AnimationClip;
            var talk = so.FindProperty("talkClip").objectReferenceValue as AnimationClip;
            Expect(sit != null && sit.legacy && talk != null && talk.legacy, id + " clips legacy");
            n++;
        }

        Debug.Log($"CHECK characters imported: {n} of {App.Characters.Length + 1} (patients and the doctor)");

        // Look + pinch on App's own start panel: two taps in a row on ">" both press it and change the patient
        // (the first build took only the very first pinch of the session).
        PlayerPrefs.DeleteKey("character_id");   // start from "the case's own patient"
        var app = new GameObject("App").AddComponent<App>();
        typeof(App).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, null);
        Canvas.ForceUpdateCanvases();
        Physics.SyncTransforms();
        var next = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
            .FirstOrDefault(b => b.GetComponentInChildren<TMPro.TMP_Text>().text == ">");
        var label = (TMPro.TMP_Text)typeof(App).GetField("patientLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
        Expect(next != null && label != null, "start panel has the > button");
        if (next != null && label != null) {
            var rt = (RectTransform)next.transform;
            var eyes = new Vector3(0, 1.6f, 0);
            var ray = new Ray(eyes, rt.TransformPoint(rt.rect.center) - eyes);
            var tap = app.GetComponent<SpatialTap>();
            var before = label.text;
            var first = tap.Tap(ray);
            var middle = label.text;
            var second = tap.Tap(ray);
            Debug.Log($"CHECK tap: {first} | {second} | label {before} -> {middle} -> {label.text}");
            Expect(first.Contains("\">\"") && second.Contains("\">\"") && before != middle && middle != label.text, "pinch on > twice changes the patient twice");

            // What the headset sends, frame by frame, for two pinches in a row: both carry interaction id 1 (a reused
            // slot), phases Began, Moved, Ended, None. Each pinch must press once, and a Began seen twice only once.
            var last = UnityEngine.XR.VisionOS.VisionOSSpatialPointerPhase.None;
            int presses = 0;
            var start = label.text;
            // the debounce counts real time; this check sends both pinches at once, so it steps the clock past it
            typeof(SpatialTap).GetField("lastTap", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -10f);
            foreach (var phase in new[] { 1, 1, 2, 3, 0, 1, 2, 2, 3, 0 }) {
                if (phase == 1 && presses == 1) typeof(SpatialTap).GetField("lastTap", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -10f);
                var st = new UnityEngine.XR.VisionOS.InputDevices.VisionOSSpatialPointerState {
                    interactionId = 1, startRayOrigin = ray.origin, startRayDirection = ray.direction.normalized,
                    phaseId = (byte)phase, isTracked = true, kindId = (byte)UnityEngine.XR.VisionOS.InputDevices.VisionOSSpatialPointerKind.IndirectPinch };
                if (tap.Handle(st, ref last) != null) presses++;
            }
            Debug.Log($"CHECK pinch sequence: {presses} presses, label {start} -> {label.text}");
            Expect(presses == 2 && label.text.Contains("(5/"), "two pinches with the same interaction id press > twice");
            Expect(!label.text.Contains("Doctor") && !label.text.ToLower().Contains("simple"), "picker has no doctor and no simple figure");

            // The app's keyboard: a pinch on the address field opens it, keys type, Enter closes it.
            var url = (TMPro.TMP_InputField)typeof(App).GetField("urlField", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
            url.text = "";
            Ray At(Component c) { var r = (RectTransform)c.transform; return new Ray(eyes, r.TransformPoint(r.rect.center) - eyes); }
            tap.Tap(At(url));
            var kb = VirtualKeyboard.Instance;
            Expect(kb != null && kb.IsOpen, "pinch on a text field opens the app's keyboard");
            if (kb != null && kb.IsOpen) {
                Physics.SyncTransforms();
                UnityEngine.UI.Button Key(string l) => kb.GetComponentsInChildren<UnityEngine.UI.Button>().First(b => b.GetComponentInChildren<TMPro.TMP_Text>().text == l);
                var typed = string.Join(" | ", new[] { "1", "9", "2", ".", "Enter" }.Select(k => tap.Tap(At(Key(k)))));
                Debug.Log($"CHECK keyboard: {typed} -> \"{url.text}\", open {kb.IsOpen}");
                Expect(url.text == "192." && !kb.IsOpen, "keys type into the field and Enter closes the keyboard");
            }
        }
        // Restart: the student stands anywhere, looking any way; afterwards they are at the origin facing the patient (+z),
        // and the app is built again in place (no scene load, which stopped head tracking on the headset).
        var xo = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xo == null) {
            xo = new GameObject("XR Origin").AddComponent<Unity.XR.CoreUtils.XROrigin>();
            var offset = new GameObject("Camera Offset").transform; offset.SetParent(xo.transform, false);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; camGo.transform.SetParent(offset, false);
            xo.Camera = camGo.AddComponent<Camera>();
        }
        // The student walked off and looks at the Restart button (up left), the room was turned by an older version.
        var head = Camera.main.transform;
        head.localPosition = new Vector3(1.0f, 1.15f, 2.0f); head.localRotation = Quaternion.Euler(-10, -37, 0);
        xo.transform.rotation = Quaternion.Euler(0, 40, 0);
        var apps = Object.FindObjectsByType<App>(FindObjectsSortMode.None);
        var oldApp = apps.Length > 0 ? apps[0] : null;
        var oldRoom = oldApp != null ? (Room)typeof(App).GetField("room", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(oldApp) : null;
        void PressRestart() {
            var a = Object.FindObjectsByType<App>(FindObjectsSortMode.None);
            if (a.Length > 0) typeof(App).GetMethod("Restart", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(a[0], null);
            else App.Recenter();
        }
        PressRestart();
        var pose1 = (xo.transform.position, xo.transform.rotation);
        Debug.Log($"CHECK restart: head {head.position:F2}, room turn {Quaternion.Angle(xo.transform.rotation, Quaternion.identity):0.0} deg");
        Expect(Mathf.Abs(head.position.x) < 0.01f && Mathf.Abs(head.position.z) < 0.01f && Mathf.Abs(head.position.y - 1.15f) < 0.01f
               && Quaternion.Angle(xo.transform.rotation, Quaternion.identity) < 0.01f, "restart puts the student on their spot, room as at the start");
        PressRestart(); PressRestart(); PressRestart();
        Expect(Vector3.Distance(pose1.Item1, xo.transform.position) < 0.001f && Quaternion.Angle(pose1.Item2, xo.transform.rotation) < 0.01f,
               "pressing restart four times gives the same view (nothing turns)");
        Expect(oldApp == null && oldRoom == null && Object.FindObjectsByType<App>(FindObjectsSortMode.None).Length == 1, "restart builds the app again in place");

        Debug.Log(failed == 0 ? "CHECKS PASSED" : $"CHECKS FAILED: {failed}");
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
