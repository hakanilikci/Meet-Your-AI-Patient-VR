using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// The whole app flow: pick a case → talk (voice or typed) → end → feedback.
/// Builds its panel in code so the scene holds only this component (no medical facts, hard rule 2).
public class App : MonoBehaviour {
    // Default server = the Mac that built the app (Setup writes its address into Resources/server.txt);
    // change it in the panel, it is remembered once a connection works.
    static string[] DefaultServers => (Resources.Load<TextAsset>("server")?.text ?? "http://127.0.0.1:8000")
        .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    // Student's own lines for when speech does not work (hard rule 5). They are questions, not medical facts;
    // the first six lines of docs/demo-script.md, which both the live and the scripted patient answer from the case.
    static readonly (string label, string text)[] Quick = {
        ("Introduce myself", "Hello, I'm Anna, a medical student. What brings you in today?"),
        ("How long?", "How long has this been going on?"),
        ("Where is the pain?", "Where exactly is the pain?"),
        ("Dizzy / tired?", "Do you feel dizzy when you stand up? And how tired are you?"),
        ("Skin changes?", "Have you noticed any change in your skin?"),
        ("What worries you?", "What worries you most about this?"),
    };

    static readonly Vector3 PatientPosition = new Vector3(0, 0, 1.6f);
    int choice;   // index into Choices: 0 = the case's own avatar
    string shownId;
    Room room;
    TMP_Text patientLabel;

    IAIClient ai;
    IPatient patient;
    string sessionId, patientName = "Patient";
    readonly List<string> lines = new();
    bool busy, recording;
    AudioClip micClip;

    GameObject pickPanel, talkPanel, feedbackPanel, panelRoot, toolbarRoot;
    VirtualKeyboard keyboard;
    Transform caseList;
    TMP_InputField urlField, textField;
    TMP_Text status, header, transcript, talkLabel, feedbackText;

    void Start() {
        StartLog();
        Debug.Log("[MedicalVR] App.Start");
        Application.RequestUserAuthorization(UserAuthorization.Microphone);
        SpeechInput.Authorize();
        // Full VR: the student stands at the origin facing +z (the immersive space starts at their feet).
        gameObject.AddComponent<SpatialTap>();   // look + pinch on the panel
        room = Room.Build();
        var saved = PlayerPrefs.GetString("character_id", "case");
        choice = Math.Max(0, Array.FindIndex(Choices, c => c.id == saved));
        ShowPatient(choice == 0 ? Characters[0].id : Choices[choice].id);
        if (patient is Component pc) room.Light(pc.gameObject);
        BuildUI();
        Show(pickPanel);
        // Simulator smoke test without clicking: SIMCTL_CHILD_MEDICALVR_AUTOTEST=1 xcrun simctl launch <device> de.medicalvr.aipatient
        if (Environment.GetEnvironmentVariable("MEDICALVR_AUTOTEST") == "1") _ = AutoTest();
        else if (!Application.isBatchMode) Invoke(nameof(AutoConnect), 1f);   // the case list comes up without a tap
        Debug.Log($"[MedicalVR] scene ready: {FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length} renderers, camera {(Camera.main ? Camera.main.name : "none")}");
        Invoke(nameof(LogHead), 3f); Invoke(nameof(LogHead), 10f);   // is the head tracked yet?
    }

    // The app's own lines and errors also go to Documents/medicalvr.log (the run before: medicalvr.prev.log, so a
    // freeze can be looked at after the restart). The Mac reads them with
    // xcrun devicectl device copy from --device <udid> --domain-type appDataContainer --domain-identifier <bundle id> --source Documents/medicalvr.log --destination /tmp/medicalvr.log
    static string logFile;
    static void StartLog() {
        if (logFile != null) return;   // once per launch: Restart builds the app again in the same run
        var dir = Application.persistentDataPath;
        logFile = System.IO.Path.Combine(dir, "medicalvr.log");
        try {
            if (System.IO.File.Exists(logFile)) System.IO.File.Copy(logFile, System.IO.Path.Combine(dir, "medicalvr.prev.log"), true);
            System.IO.File.WriteAllText(logFile, "");
        } catch { }
        Application.logMessageReceived += (msg, stack, type) => {
            if (!msg.StartsWith("[MedicalVR]") && type != LogType.Exception && type != LogType.Error) return;
            var at = type == LogType.Exception ? " at " + stack.Split('\n')[0] : "";
            try { System.IO.File.AppendAllText(logFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}{at}\n"); } catch { }
        };
    }

    /// avatar's characters (avatar/avatar_show; Rocketbox models imported by RocketboxImport) and Hannah (AvatarImport).
    public static readonly (string id, string label)[] Characters = {
        ("patient_male_older", "Man, older (Male_Adult_05)"),
        ("patient_male_older_2", "Man, older 2 (Male_Adult_14)"),
        ("patient_male_south_asian", "Man, South Asian (Male_Adult_17)"),
        ("patient_male_young", "Man, young (Male_Adult_06)"),
        ("patient_female_older", "Woman, older (Female_Adult_09)"),
        ("patient_female_elderly", "Woman, elderly (Female_Adult_02)"),
        ("patient_female_young", "Woman, young (Female_Adult_17)"),
        ("patient_female_young_2", "Woman, young 2 (Female_Adult_15)"),
        ("hannah", "Hannah (Ara)"),
    };
    /// avatar's attending (Medical_Male_03): stands by the desk while the feedback is shown, never a patient.
    public const string Doctor = "attending";
    public static readonly Vector3 DoctorPosition = new Vector3(0.85f, 0, 1.75f);   // by the desk, beside the patient
    GameObject doctor;

    // Picker entries: the case's own avatar, then every imported character. (The simple figure stays only as the
    // fallback when no character is imported, hard rule 5.)
    static (string id, string label)[] choices;
    static (string id, string label)[] Choices => choices ??= new[] { ("case", "The case's own patient") }
        .Concat(Characters.Where(c => Resources.Load<GameObject>(c.id) != null)).ToArray();

    /// The character `id` seated on the armchair; the next imported one if it is missing, the simple figure last.
    public static IPatient CreatePatient(Vector3 position, string id) {
        GameObject prefab = Resources.Load<GameObject>(id);
        if (prefab == null)
            foreach (var c in Characters) if ((prefab = Resources.Load<GameObject>(c.id)) != null) break;
        if (prefab == null) return PlaceholderPatient.Create(position, Vector3.zero);
        var go = Instantiate(prefab, new Vector3(position.x, 0, position.z), Quaternion.LookRotation(-Vector3.forward));   // faces the student
        go.name = prefab.name;
        var avatar = go.GetComponent<AvatarPatient>();
        avatar.Init();
        // seated clips put the hips away from the origin: move the origin so the hips are 1.35 m from the student
        go.transform.position = new Vector3(position.x, 0, 1.35f + avatar.HipsZ);
        Room.PatientChair(go.transform, avatar.SeatHeight, avatar.SeatZ);
        Debug.Log($"[MedicalVR] patient: {prefab.name}, seat {avatar.SeatHeight:0.00} m, hips z {avatar.HipsZ:0.00}");
        return avatar;
    }

    /// The attending, standing at `at` and facing the student (null if he is not imported).
    public static AvatarPatient CreateDoctor(Vector3 at) {
        var prefab = Resources.Load<GameObject>(Doctor);
        if (prefab == null) return null;
        var flat = -at; flat.y = 0;
        var go = Instantiate(prefab, new Vector3(at.x, 0, at.z), Quaternion.LookRotation(flat));
        go.name = Doctor;
        var doc = go.GetComponent<AvatarPatient>();
        doc.Init();
        return doc;
    }

    // The doctor appears with the feedback and gestures for a few seconds as if explaining it; he leaves with it.
    void ShowDoctor(bool on) {
        Kill(doctor);
        var doc = on ? CreateDoctor(DoctorPosition) : null;
        doctor = doc != null ? doc.gameObject : null;
        if (doc == null) return;
        room.Light(doctor);
        doc.Gesture(6f);
    }

    void ShowPatient(string id) {
        if (id == shownId && patient != null) return;
        if (patient is Component c) Kill(c.gameObject);
        patient = CreatePatient(PatientPosition, id);
        shownId = id;
        if (patient is Component pc) room.Light(pc.gameObject);
    }

    string PatientLabel() {
        var now = Characters.FirstOrDefault(c => c.id == shownId).label ?? "simple figure";
        var n = $"({choice + 1}/{Choices.Length})";
        return choice == 0 ? $"Patient: the case's own, now {now} {n}" : $"Patient: {Choices[choice].label} {n}";
    }

    // < > on the start panel: swaps only the body, the conversation is unaffected.
    void PickPatient(int step) {
        choice = (choice + step + Choices.Length) % Choices.Length;
        PlayerPrefs.SetString("character_id", Choices[choice].id);
        if (choice != 0) ShowPatient(Choices[choice].id);
        patientLabel.text = PatientLabel();
        Debug.Log($"[MedicalVR] picked {Choices[choice].id}, showing {shownId}");
    }

    void AutoConnect() { if (!busy && caseList.childCount == 0) Connect(false); }

    // where the headset puts the student's eyes (the room assumes the floor at y 0)
    void LogHead() {
        var cam = Camera.main;
        if (cam != null) Debug.Log($"[MedicalVR] head at {cam.transform.position:F2}, camera offset {(cam.transform.parent ? cam.transform.parent.localPosition.ToString("F2") : "none")}");
    }

    // ---------- flow ----------

    async void Connect(bool mock) {
        if (busy) return;
        await Busy("Looking for the ai server...", async () => {
            if (!mock) {
                // the typed address first, then the Mac's other addresses: the network may differ from yesterday.
                // A bad address used to throw outside Busy and left the app "busy" for good (it looked frozen).
                string found = null;
                foreach (var url in new[] { urlField.text.Trim() }.Concat(DefaultServers).Distinct()) {
                    Status($"Looking for the ai server at {url} ...");
                    bool ok = false;
                    try { ok = await HttpAIClient.Answers(url); } catch (Exception e) { Debug.Log($"[MedicalVR] bad address {url}: {e.Message}"); }
                    if (ok) { found = url; break; }
                    Debug.Log("[MedicalVR] no ai server at " + url);
                }
                if (found == null) { Status("<color=#C0392B>No ai server found.</color> Is it running on the Mac, and is the headset on the same Wi-Fi?"); return; }
                urlField.text = found;
                Debug.Log("[MedicalVR] ai server " + found);
            }
            ai = mock ? new MockAIClient() : new HttpAIClient(urlField.text);
            Status("Loading cases...");
            var cases = await ai.Cases();
            foreach (Transform c in caseList) Destroy(c.gameObject);
            foreach (var card in cases) {
                var label = $"{card.title}\n<size=70%>{card.name}, {card.age} · {card.difficulty}</size>";
                Btn(caseList, label, () => StartCase(card), 110);
            }
            if (!mock) PlayerPrefs.SetString("server_ok", urlField.text);
            Status(cases.Length == 0 ? "No cases on the server." : "Pick a case.");
        });
    }

    async void StartCase(CaseCard card) {
        if (busy) return;
        await Busy("Starting session...", async () => {
            var s = await ai.StartSession(card.id);
            sessionId = s.session_id;
            var c = s.@case ?? card;
            patientName = string.IsNullOrEmpty(c.name) ? "Patient" : c.name;
            header.text = $"<b>{c.title}</b>\n<size=75%>{c.door_note}</size>";
            lines.Clear(); transcript.text = "";
            // the case names its patient's avatar (ai: GET /cases "avatar"); used unless one was picked
            if (choice == 0 && !string.IsNullOrEmpty(c.avatar)) { ShowPatient(c.avatar); patientLabel.text = PatientLabel(); }
            patient.SetEmotion("neutral");
            Show(talkPanel);
            Status("Say hello: press Talk, or type below.");
        });
    }

    // Talk: the headset's own speech-to-text when it is there (no key needed on the ai laptop), else the recording
    // goes to ai's speech-to-text. Either way the typed box and the question buttons stay as fallback (hard rule 5).
    void ToggleTalk() {
        if (busy) return;
        if (!recording) {
            if (SpeechInput.Start(gameObject)) { recording = true; talkLabel.text = "Stop & send"; Status("Listening..."); return; }
            if (Microphone.devices.Length == 0) { Status("No microphone found, type or tap a question instead."); return; }
            micClip = Microphone.Start(null, false, 30, 24000);
            recording = true; talkLabel.text = "Stop & send"; Status("Listening...");
            return;
        }
        recording = false; talkLabel.text = "Talk";
        if (micClip == null) { SpeechInput.Stop(); Status("..."); return; }   // text comes back in OnSpeechResult
        int pos = Microphone.GetPosition(null);
        Microphone.End(null);
        var clip = micClip; micClip = null;
        if (pos <= 0) { Status("Nothing recorded, try again or type."); return; }
        var samples = new float[pos * clip.channels];
        clip.GetData(samples, 0);
        _ = Turn(null, Wav.FromSamples(samples, clip.frequency));
    }

    // Called by SpeechBridge.mm (UnitySendMessage) with what the student said.
    void OnSpeechResult(string text) {
        if (string.IsNullOrWhiteSpace(text)) { Status("I did not catch that. Try again, type or tap a question."); return; }
        _ = Turn(text.Trim(), null);
    }

    void SendTyped() {
        var t = textField.text.Trim();
        if (t.Length == 0 || busy) return;
        textField.text = "";
        _ = Turn(t, null);
    }

    async Task Turn(string text, byte[] wav) {
        await Busy("The patient is answering...", async () => {
            var r = await ai.Turn(sessionId, text, wav);
            Say("You", r.student_text);
            Say(patientName, r.patient_text);   // subtitles always, audio is a bonus (hard rule 5)
            patient.SetEmotion(r.emotion);
            var clip = await ReplyAudio.Load(r);
            // the headset log (Documents/medicalvr.log) shows whether the voice arrived and played
            var sent = string.IsNullOrEmpty(r.audio_base64) ? "no audio sent" : $"{r.audio_format}, {r.audio_base64.Length * 3 / 4 / 1024} KB";
            Debug.Log($"[MedicalVR] reply audio: {sent} -> {(clip != null ? $"playing {clip.length:0.0} s" : "subtitles only")}");
            patient.PlayReply(clip);
            Status(clip == null ? "(no audio, subtitles only)" : "");
        });
    }

    async void End() {
        if (busy) return;
        if (recording) { if (micClip != null) Microphone.End(null); else SpeechInput.Stop(); micClip = null; recording = false; talkLabel.text = "Talk"; }
        await Busy("Grading the conversation...", async () => {
            var fb = await ai.End(sessionId);
            feedbackText.text = FormatFeedback(fb);
            Show(feedbackPanel);
            ShowDoctor(true);
            Status("");
        });
    }

    /// Covered items always show the student's own words; no evidence, no credit (hard rule 3).
    static string FormatFeedback(Feedback fb) {
        var sb = new System.Text.StringBuilder();
        bool vsReference = fb.graded_against == "reference";   // scored against a model consultation of this case
        sb.Append($"<b>Score: {fb.overall_percent:0}%</b>");
        if (vsReference) sb.Append($" <size=70%>of the model consultation ({fb.rubric_percent:0}% of the full checklist)</size>");
        if (fb.graded_by == "scripted") sb.Append(" <size=70%>(offline grader)</size>");
        sb.AppendLine();
        var items = fb.items ?? Array.Empty<FeedbackItem>();
        // Covered items always show the student's own words; no evidence, no credit (hard rule 3).
        sb.AppendLine("\n<b>Covered</b>");
        foreach (var i in items)
            if (i.achieved && !string.IsNullOrEmpty(i.evidence))
                sb.AppendLine($"<color=#2E8B57>[x]</color> {i.text}\n    <i><size=80%>\"{i.evidence}\" ({i.at_s:0} s)</size></i>");
        sb.AppendLine("\n<b>Missed</b>");
        foreach (var i in items)
            if ((!i.achieved || string.IsNullOrEmpty(i.evidence)) && (!vsReference || i.in_reference)) {
                sb.AppendLine($"<color=#C0392B>[ ]</color> {i.text}");
                if (!string.IsNullOrEmpty(i.reference_line)) sb.AppendLine($"    <i><size=80%>Try: \"{i.reference_line}\"</size></i>");
            }
        foreach (var m in fb.missed_facts ?? Array.Empty<MissedFact>())
            if (m.red_flag) sb.AppendLine($"<color=#C0392B>[!]</color> Red flag not found: {m.text}");
        if (fb.suggestions?.Length > 0) {
            sb.AppendLine("\n<b>Suggestions</b>");
            foreach (var s in fb.suggestions) sb.AppendLine("- " + s);
        }
        return sb.ToString();
    }

    void Say(string who, string text) {
        if (string.IsNullOrEmpty(text)) return;
        lines.Add($"<b>{who}:</b> {text}");
        if (lines.Count > 8) lines.RemoveAt(0);   // shortcut: last 8 lines, add scrolling if needed
        transcript.text = string.Join("\n", lines);
    }

    async Task Busy(string msg, Func<Task> work) {
        busy = true; busySince = Time.time; Status(msg);
        try { await work(); }
        catch (Exception e) {
            if (this == null) return;   // restarted meanwhile: the answer belongs to the old session
            var lost = e.Message.Contains("Cannot connect") || e.Message.Contains("timeout") || e.Message.Contains("resolve");
            Status(lost ? "<color=#C0392B>No answer from the ai server.</color> Is the headset on the Mac's Wi-Fi? Tap Restart to start over."
                        : "<color=#C0392B>Error:</color> " + e.Message);
            Debug.LogException(e);
        }
        finally { busy = false; }
    }

    // While the app waits for ai, the status line counts the seconds, so a slow answer does not look like a freeze.
    float busySince;
    string statusText = "";
    void Status(string s) { statusText = s; if (status != null) status.text = s; }
    void Update() {
        if (busy && status != null && Time.time - busySince > 3f)
            status.text = $"{statusText}  <color=#888888>{Time.time - busySince:0} s (Restart starts over)</color>";
    }
    void Show(GameObject panel) {
        if (keyboard != null) keyboard.Close();
        foreach (var p in new[] { pickPanel, talkPanel, feedbackPanel }) p.SetActive(p == panel);
    }

    static void Kill(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    // ---------- Restart / Quit ----------

    /// Starts the app over: new session, panel, room and patient, with the patient right in front of the student.
    /// In place, not by loading the scene again: a new scene also starts a new AR session, and visionOS then stops
    /// tracking the head (everything moved with the student's eyes). A request still running is simply dropped.
    void Restart() {
        Debug.Log("[MedicalVR] restart");
        if (recording) { if (micClip != null) Microphone.End(null); else SpeechInput.Stop(); recording = false; }
        Recenter();
        foreach (var o in new UnityEngine.Object[] { room != null ? room.gameObject : null, doctor, panelRoot, toolbarRoot, keyboard != null ? keyboard.gameObject : null })
            Kill(o);   // the patient and the doctor are inside the room
        new GameObject("App").AddComponent<App>();   // its Start builds everything again
        Kill(gameObject);
    }

    /// Back to the start: the room turned as when the app opened (the patient in front of the student, as visionOS
    /// placed it then), and the student's head over their spot 1.35 m from the patient. Absolute, so pressing Restart
    /// again and again gives the same view. It never turns the room by where the student looks: they look at the
    /// Restart button when they press it, and the first version turned the room by that much on every press.
    public static void Recenter() {
        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        var cam = Camera.main;
        if (origin == null || cam == null) return;
        if (origin.Origin == null) origin.Origin = origin.gameObject;
        if (origin.Camera == null) origin.Camera = cam;
        origin.transform.rotation = Quaternion.identity;   // the launch orientation
        origin.MoveCameraToWorldLocation(new Vector3(0, cam.transform.position.y, 0));
        Debug.Log($"[MedicalVR] reset to start: head {cam.transform.position:F2}, origin {origin.transform.position:F2}");
    }

    void Quit() {
        Debug.Log("[MedicalVR] quit");
        Application.Quit();   // visionOS: the app ends and the headset goes back to Home
    }

    // Quit needs a second tap within 3 s, so a stray pinch does not close the app.
    float confirmUntil;
    TMP_Text confirming;
    void Confirm(TMP_Text label, string ask, Action act) {
        if (confirming == label && Time.time < confirmUntil) { act(); return; }
        if (confirming != null) confirming.text = confirmText;
        confirming = label; confirmText = label.text; confirmUntil = Time.time + 3f;
        label.text = ask;
        CancelInvoke(nameof(EndConfirm)); Invoke(nameof(EndConfirm), 3f);
    }
    string confirmText;
    void EndConfirm() { if (confirming != null) confirming.text = confirmText; confirming = null; }

    // Walks the whole flow on the mock: cases → session → typed turn → end → feedback.
    async Task AutoTest() {
        await Task.Delay(3000);
        var server = Environment.GetEnvironmentVariable("MEDICALVR_SERVER");
        if (!string.IsNullOrEmpty(server)) urlField.text = server;
        Connect(string.IsNullOrEmpty(server)); while (busy || caseList.childCount == 0) await Task.Delay(200);
        Debug.Log("[MedicalVR] autotest: cases " + caseList.childCount);
        await Task.Delay(4000);
        caseList.GetChild(Mathf.Min(caseList.childCount - 1, 1)).GetComponent<Button>().onClick.Invoke(); while (busy || !talkPanel.activeSelf) await Task.Delay(200);
        textField.text = Quick[0].text; SendTyped();
        await Task.Delay(300); while (busy) await Task.Delay(200);
        Debug.Log("[MedicalVR] autotest: transcript " + lines.Count + " lines");
        await Task.Delay(9000);   // let her finish speaking, then take the next question
        textField.text = Quick[1].text; SendTyped();
        await Task.Delay(300); while (busy) await Task.Delay(200);
        Debug.Log("[MedicalVR] autotest: transcript " + lines.Count + " lines");
        await Task.Delay(9000);
        End(); while (busy || !feedbackPanel.activeSelf) await Task.Delay(200);
        Debug.Log("[MedicalVR] autotest: feedback shown");
    }

    // ---------- UI built in code ----------

    void BuildUI() {
        // World-space panel to the student's right, between them and the patient, turned to face them.
        var canvasGo = new GameObject("Panel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)canvasGo.transform;
        rt.sizeDelta = new Vector2(800, 1000);
        rt.localScale = Vector3.one * 0.0007f;  // 0.56 m × 0.7 m
        rt.position = new Vector3(-0.8f, 1.2f, 1.25f);   // to the student's left: the doctor's desk is on the right
        rt.rotation = Quaternion.LookRotation(rt.position - new Vector3(0, 1.2f, 0));

        // visionOS gaze + pinch hits colliders; the panel needs one to receive taps.
        canvasGo.AddComponent<BoxCollider>().size = new Vector3(800, 1000, 1);
        keyboard = VirtualKeyboard.Create(rt);

        // Restart and Quit, just above the panel, in every step of the flow. (If the app itself hangs, only the
        // headset can end it: hold the top button and the Digital Crown, then Force Quit.)
        panelRoot = canvasGo;
        var bar = toolbarRoot = new GameObject("Toolbar", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        bar.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var brt = (RectTransform)bar.transform;
        brt.sizeDelta = new Vector2(800, 84);
        brt.localScale = rt.localScale; brt.rotation = rt.rotation;
        brt.position = rt.position + Vector3.up * ((rt.sizeDelta.y + brt.sizeDelta.y) * rt.localScale.y / 2 + 0.04f);
        bar.AddComponent<BoxCollider>().size = new Vector3(800, 84, 1);
        var tools = Row(bar.transform, 84);
        Stretch((RectTransform)tools);
        var restart = Btn(tools, "Restart", null, 84);
        restart.GetComponent<Image>().color = new Color(0.25f, 0.32f, 0.45f);
        restart.onClick.AddListener(Restart);   // one tap: nothing is lost that cannot be started again
        var quit = Btn(tools, "Quit app", null, 84);
        quit.GetComponent<Image>().color = new Color(0.62f, 0.2f, 0.2f);
        var quitLabel = quit.GetComponentInChildren<TMP_Text>();
        quit.onClick.AddListener(() => Confirm(quitLabel, "Tap again to quit", Quit));

        var bg = Img(rt, new Color(1, 1, 1, 0.92f));
        Stretch(bg.rectTransform);
        status = Text(rt, "", 26, new Color(0.3f, 0.3f, 0.3f));
        var st = status.rectTransform;
        st.anchorMin = new Vector2(0, 0); st.anchorMax = new Vector2(1, 0); st.pivot = new Vector2(0.5f, 0);
        st.sizeDelta = new Vector2(-40, 60); st.anchoredPosition = new Vector2(0, 15);

        pickPanel = Panel(rt);
        Text(pickPanel.transform, "<b>Meet Your AI Patient</b>", 44);
        Text(pickPanel.transform, "ai server address", 24, Color.gray);
        urlField = Field(pickPanel.transform, "http://<ai-laptop-ip>:8000");
        urlField.text = PlayerPrefs.GetString("server_ok", DefaultServers[0]);
        var row = Row(pickPanel.transform);
        Btn(row, "Connect", () => Connect(false));
        Btn(row, "Use mock", () => Connect(true));
        var pick = Row(pickPanel.transform, 60);
        var prev = Btn(pick, "<", () => PickPatient(-1), 60);
        prev.GetComponent<LayoutElement>().preferredWidth = 80; prev.GetComponent<LayoutElement>().flexibleWidth = 0;
        patientLabel = Text(pick, PatientLabel(), 24);
        patientLabel.alignment = TextAlignmentOptions.Center;
        patientLabel.GetComponent<LayoutElement>().flexibleWidth = 1;
        var next = Btn(pick, ">", () => PickPatient(1), 60);
        next.GetComponent<LayoutElement>().preferredWidth = 80; next.GetComponent<LayoutElement>().flexibleWidth = 0;
        pick.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        caseList = Panel(pickPanel.transform, flexible: true).transform;

        talkPanel = Panel(rt);
        header = Text(talkPanel.transform, "", 30);
        transcript = Text(talkPanel.transform, "", 26);
        transcript.GetComponent<LayoutElement>().flexibleHeight = 1;
        var talkBtn = Btn(talkPanel.transform, "Talk", ToggleTalk, 100);
        talkLabel = talkBtn.GetComponentInChildren<TMP_Text>();
        var typed = Row(talkPanel.transform);
        textField = Field(typed, "or type your question");
        textField.GetComponent<LayoutElement>().flexibleWidth = 1;
        textField.onSubmit.AddListener(_ => SendTyped());
        Btn(typed, "Send", SendTyped).GetComponent<LayoutElement>().preferredWidth = 160;
        Text(talkPanel.transform, "Or tap a question", 22, Color.gray);
        for (int i = 0; i < Quick.Length; i += 2) {
            var qrow = Row(talkPanel.transform, 62);
            for (int j = i; j < Math.Min(i + 2, Quick.Length); j++) {
                var q = Quick[j];
                var b = Btn(qrow, q.label, () => { if (!busy) _ = Turn(q.text, null); }, 62);
                b.GetComponent<Image>().color = new Color(0.35f, 0.5f, 0.7f);
                b.GetComponentInChildren<TMP_Text>().fontSize = 24;
            }
        }
        Btn(talkPanel.transform, "End conversation", End);

        feedbackPanel = Panel(rt);
        Text(feedbackPanel.transform, "<b>Feedback</b>", 40);
        feedbackText = Text(feedbackPanel.transform, "", 22);
        feedbackText.GetComponent<LayoutElement>().flexibleHeight = 1;
        Btn(feedbackPanel.transform, "New conversation", () => { ShowDoctor(false); Show(pickPanel); });
    }

    internal static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static GameObject Panel(Transform parent, bool flexible = false) {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = 14; v.childControlWidth = v.childControlHeight = true; v.childForceExpandHeight = false;
        if (parent is RectTransform pr && pr.GetComponent<Canvas>() != null) {
            var r = (RectTransform)go.transform; Stretch(r);
            r.offsetMin = new Vector2(40, 90); r.offsetMax = new Vector2(-40, -40);
        }
        if (flexible) go.GetComponent<LayoutElement>().flexibleHeight = 1;
        return go;
    }

    internal static Transform Row(Transform parent, float height = 70) {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var h = go.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 14; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = false;
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return go.transform;
    }

    internal static Image Img(Transform parent, Color c) {
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c;
        return img;
    }

    internal static TMP_Text Text(Transform parent, string s, float size, Color? color = null) {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = size; t.color = color ?? new Color(0.1f, 0.1f, 0.15f);
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    internal static Button Btn(Transform parent, string label, UnityAction onClick, float height = 70) {
        var img = Img(parent, new Color(0.18f, 0.42f, 0.75f));
        img.gameObject.name = "Button";
        img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var b = img.gameObject.AddComponent<Button>();
        if (onClick != null) b.onClick.AddListener(onClick);
        var t = Text(img.transform, label, 28, Color.white);
        t.alignment = TextAlignmentOptions.Center;
        Stretch(t.rectTransform);
        return b;
    }

    static TMP_InputField Field(Transform parent, string placeholder) {
        var img = Img(parent, new Color(0.93f, 0.94f, 0.96f));
        img.gameObject.name = "Input";
        img.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(img.transform, false);
        var ar = (RectTransform)area.transform; Stretch(ar); ar.offsetMin = new Vector2(16, 6); ar.offsetMax = new Vector2(-16, -6);
        var ph = Text(area.transform, placeholder, 26, Color.gray); Stretch(ph.rectTransform);
        var tx = Text(area.transform, "", 26); Stretch(tx.rectTransform);
        ph.alignment = tx.alignment = TextAlignmentOptions.Left;
        var f = img.gameObject.AddComponent<TMP_InputField>();
        f.textViewport = ar; f.textComponent = tx; f.placeholder = ph;
        return f;
    }
}
