using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MedicalVR.PatientAvatar;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Brings avatar's Hannah (avatar/generated_avatar, contract: avatar/CONTRACT.md v2) into the project:
// the model with its textures, her seated idle and talking clips, and the prefab Resources/patient_female_older
// with avatar's PatientAvatarController and ui's AvatarPatient. Nothing under Assets/Avatar/Generated goes to git.
//   Unity -batchmode -projectPath ui/MedicalVR -executeMethod AvatarImport.Run      (add -nographics to skip the render check)
//
// The clip files ("Hannah@sit_idle.fbx", skeleton only) import with zero takes in Unity 6000.6.9f1 although each
// holds one valid AnimStack. ui/tools/fbx_anims_to_json.py reads their curves; this file turns them into clips.
public static class AvatarImport {
    const string Source = "../../avatar/generated_avatar";
    const string Out = "Assets/Avatar/Generated";
    const string ModelPath = Out + "/Hannah.fbx";
    // "hannah": avatar's Ara model; the Rocketbox characters use avatar's logical names (patient_female_older is Female_Adult_09)
    const string PrefabPath = Out + "/Resources/hannah.prefab";
    public static readonly string[] ClipNames = { "sit_idle", "talk_gestures" };

    // Bones the talking clip may move while she sits (the rest keeps the seated pose).
    static readonly string[] UpperBody = { "clavicle", "shoulder", "upperarm", "lowerarm", "wrist", "finger", "metacarpal", "neck" };

    [Serializable] class Bone { public string path; public float[] t, q, p; }
    [Serializable] class ClipData { public string name; public float length; public Bone[] bones; }
    [Serializable] class BindCheck { public string name; public float[] q; }
    [Serializable] class AnimFile { public ClipData[] clips; public BindCheck[] bindCheck; }

    public static void Run() { Import(); RenderCheck(); if (Application.isBatchMode) EditorApplication.Exit(0); }
    public static void RenderCheckOnly() { RenderCheck(); if (Application.isBatchMode) EditorApplication.Exit(0); }

    [MenuItem("MedicalVR/Import avatar (Hannah)")]
    public static void Import() {
        var model = Path.Combine(Source, "Hannah.fbx");
        var zip = Path.Combine(Source, "Hannah_anims.zip");
        if (!File.Exists(model) || !File.Exists(zip)) {
            Debug.LogWarning("AVATAR source not found (avatar/generated_avatar); the app uses the placeholder patient.");
            return;
        }
        Directory.CreateDirectory(Out + "/Resources");
        File.Copy(model, ModelPath, true);
        AssetDatabase.Refresh();

        // model: Humanoid avatar (for the bone lookup), textures out of the FBX so the materials keep them
        var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = false;
        mi.SaveAndReimport();
        Directory.CreateDirectory(Out + "/Textures");
        mi.ExtractTextures(Out + "/Textures");
        AssetDatabase.Refresh();
        mi.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"AVATAR humanoid valid: {avatar != null && avatar.isValid && avatar.isHuman}");

        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        root.name = "hannah";
        var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        var rootBone = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "root");
        var bonePrefix = AnimationUtility.CalculateTransformPath(rootBone, root.transform);   // e.g. "Armature/root"

        // clips from the curves
        var clips = MakeClips(zip, bonePrefix.Substring(0, bonePrefix.Length - "root".Length), rootBone);

        // materials: own copies so hair, brows and lashes can be cut out (their textures have alpha), and the hair
        // gets Ara's colour: Ara keeps it as a tint in Hannah.params.json ("hairColor"), not in the texture.
        Directory.CreateDirectory(Out + "/Materials");
        var hairColor = new Color(0.16f, 0.12f, 0.09f);
        try {
            using var z = ZipFile.OpenRead(zip);
            var entry = z.Entries.FirstOrDefault(e => e.Name == "Hannah.params.json");
            if (entry != null) {
                using var reader = new StreamReader(entry.Open());
                var hex = System.Text.RegularExpressions.Regex.Match(reader.ReadToEnd(), "\"hairColor\"\\s*:\\s*\"(#[0-9a-fA-F]{6})\"").Groups[1].Value;
                if (ColorUtility.TryParseHtmlString(hex, out var c)) hairColor = c;
            }
        } catch (Exception e) { Debug.LogWarning("AVATAR hair colour not read: " + e.Message); }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
            var mats = r.sharedMaterials.Select(m => {
                if (m == null) return null;
                var path = $"{Out}/Materials/{m.name}.mat";
                var copy = new Material(m);
                bool alpha = new[] { "hair", "eyebrow", "eyelash" }.Any(k => m.name.ToLower().Contains(k));
                if (alpha) {
                    copy.SetFloat("_Mode", 1); copy.SetFloat("_Cutoff", 0.5f);
                    copy.EnableKeyword("_ALPHATEST_ON"); copy.renderQueue = 2450; copy.SetOverrideTag("RenderType", "TransparentCutout");
                }
                if (m.name.ToLower().Contains("hair") || m.name.ToLower().Contains("eyebrow")) copy.color = hairColor * 1.6f;   // a little lighter: the texture is mid grey
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(copy, path);
                Debug.Log($"AVATAR material {m.name}: texture {(copy.mainTexture ? copy.mainTexture.name : "NONE")}{(alpha ? ", cutout" : "")}, colour {copy.color}");
                return copy;
            }).ToArray();
            r.sharedMaterials = mats;
        }

        var voice = root.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 1f; voice.minDistance = 1f;
        var faces = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToArray();
        foreach (var f in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) f.updateWhenOffscreen = true;   // the bounds follow the seated pose

        var ctl = root.AddComponent<PatientAvatarController>();
        var so = new SerializedObject(ctl);
        so.FindProperty("animator").objectReferenceValue = null;   // body motion comes from ui's clips; avatar's controller does face and voice
        so.FindProperty("speechAudio").objectReferenceValue = voice;
        so.FindProperty("head").objectReferenceValue = null;       // gaze: AvatarPatient (after the clip has posed the head)
        var arr = so.FindProperty("faceMeshes"); arr.arraySize = faces.Length;
        for (int i = 0; i < faces.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = faces[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        var adapter = root.AddComponent<AvatarPatient>();
        var aso = new SerializedObject(adapter);
        aso.FindProperty("controller").objectReferenceValue = ctl;
        aso.FindProperty("animator").objectReferenceValue = animator;
        aso.FindProperty("voice").objectReferenceValue = voice;
        aso.FindProperty("sitClip").objectReferenceValue = clips["sit_idle"];
        aso.FindProperty("talkClip").objectReferenceValue = clips["talk_gestures"];
        var farr = aso.FindProperty("faces"); farr.arraySize = faces.Length;
        for (int i = 0; i < faces.Length; i++) farr.GetArrayElementAtIndex(i).objectReferenceValue = faces[i];
        aso.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("AVATAR prefab ready: " + PrefabPath);
    }

    static Dictionary<string, AnimationClip> MakeClips(string zip, string pathPrefix, Transform rootBone) {
        var tmp = Path.Combine(Path.GetTempPath(), "hannah_anims");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ZipFile.ExtractToDirectory(zip, tmp);
        var json = Path.Combine(tmp, "anims.json");
        var tool = Path.GetFullPath("../tools/fbx_anims_to_json.py");
        var psi = new ProcessStartInfo("/usr/bin/python3", $"\"{tool}\" \"{Path.Combine(tmp, "Hannah")}\" \"{json}\" {string.Join(" ", ClipNames)}") {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using (var p = Process.Start(psi)) {
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
            Debug.Log("AVATAR curves: " + o.Trim().Replace("\n", " | "));
            if (p.ExitCode != 0) throw new Exception("fbx_anims_to_json.py failed");
        }
        var data = JsonUtility.FromJson<AnimFile>(File.ReadAllText(json));

        // the curves use the same axis conversion as Unity's model import, if the bind poses match
        var b = data.bindCheck.First(x => x.name == "root").q;
        var mine = new Quaternion(b[0], b[1], b[2], b[3]);
        Debug.Log($"AVATAR bind check root: unity {rootBone.localRotation} vs curves {mine}, angle {Quaternion.Angle(rootBone.localRotation, mine):0.0} deg");
        Debug.Log($"AVATAR root bind pos {rootBone.localPosition.ToString("F4")}, parent {rootBone.parent.name} pos {rootBone.parent.localPosition.ToString("F4")} rot {rootBone.parent.localRotation.eulerAngles} scale {rootBone.parent.localScale.ToString("F4")}, model root scale {rootBone.root.localScale.ToString("F4")}");

        Directory.CreateDirectory(Out + "/Clips");
        var result = new Dictionary<string, AnimationClip>();
        foreach (var c in data.clips) {
            bool upperOnly = c.name.StartsWith("talk");
            // legacy: a player can sample only legacy clips without an Animator ("Non-Legacy animations cannot be
            // sampled outside the Editor"); on the headset she stood in her T-pose.
            var clip = new AnimationClip { name = c.name, frameRate = 30, wrapMode = WrapMode.Loop, legacy = true };
            int n = 0;
            foreach (var bone in c.bones) {
                var leaf = bone.path.Substring(bone.path.LastIndexOf('/') + 1);
                if (upperOnly && !UpperBody.Any(k => leaf.Contains(k))) continue;
                var path = pathPrefix + bone.path;
                if (bone.q != null && bone.q.Length == bone.t.Length * 4) {
                    for (int a = 0; a < 4; a++) {
                        var keys = new Keyframe[bone.t.Length];
                        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(bone.t[i], bone.q[i * 4 + a]);
                        clip.SetCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[a], Smooth(keys));
                    }
                    n++;
                }
                if (!upperOnly && bone.p != null && bone.p.Length == bone.t.Length * 3) {
                    for (int a = 0; a < 3; a++) {
                        var keys = new Keyframe[bone.t.Length];
                        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(bone.t[i], bone.p[i * 3 + a]);
                        clip.SetCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[a], Smooth(keys));
                    }
                }
            }
            var asset = $"{Out}/Clips/{c.name}.anim";
            AssetDatabase.DeleteAsset(asset); AssetDatabase.CreateAsset(clip, asset);
            Debug.Log($"AVATAR clip {c.name}: {c.length:0.00} s, {n} bones");
            result[c.name] = clip;
        }
        return result;
    }

    static AnimationCurve Smooth(Keyframe[] keys) {
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        return curve;
    }

    /// Renders Hannah seated on her chair to Build/avatar_check.png, so the pose and textures can be checked without a headset.
    [MenuItem("MedicalVR/Render avatar check")]
    public static void RenderCheck() {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("AVATAR render check skipped (-nographics)"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;
        var go = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        var hannah = go.GetComponent<AvatarPatient>();
        var shots = new List<string>();
        foreach (var (label, talk, t) in new[] { ("sit", 0f, 0.5f), ("talk", 1f, 1.3f) }) {
            hannah.PoseForEditor(t, talk);
            string P(string n) { var x = go.GetComponentsInChildren<Transform>().FirstOrDefault(b => b.name == n); return x ? $"{n}{go.transform.InverseTransformPoint(x.position).ToString("F2")}" : n + "=none"; }
            Debug.Log($"AVATAR pose {label}: {P("root")} {P("lowerleg01.L")} {P("foot.L")} {P("head")} {P("hand.R")}");
            var chair = new GameObject("chair");
            chair.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
            PlaceholderPatient.MakeChair(chair.transform, hannah.SeatHeight, hannah.SeatZ);
            foreach (var view in new[] { ("front", new Vector3(0, 1.1f, 2.2f)), ("side", new Vector3(2.2f, 1.0f, 0.3f)) }) {
                var cam = new GameObject("cam").AddComponent<Camera>();
                cam.transform.position = view.Item2; cam.transform.LookAt(new Vector3(0, 0.8f, 0.1f));
                cam.fieldOfView = 45; cam.backgroundColor = new Color(0.75f, 0.78f, 0.82f); cam.clearFlags = CameraClearFlags.SolidColor;
                var light = new GameObject("light").AddComponent<Light>();
                light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 200, 0); light.intensity = 1.2f;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
                var rt = new RenderTexture(640, 800, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 800, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 640, 800), 0, 0); tex.Apply();
                var file = $"Build/avatar_check_{label}_{view.Item1}.png";
                Directory.CreateDirectory("Build"); File.WriteAllBytes(file, tex.EncodeToPNG());
                shots.Add(file);
                RenderTexture.active = null; cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cam.gameObject); UnityEngine.Object.DestroyImmediate(light.gameObject); UnityEngine.Object.DestroyImmediate(rt);
            }
            UnityEngine.Object.DestroyImmediate(chair);
        }
        Debug.Log($"AVATAR render check: {string.Join(", ", shots)}; seat {hannah.SeatHeight:0.00} m at z {hannah.SeatZ:0.00}");
        UnityEngine.Object.DestroyImmediate(go);
    }
}
