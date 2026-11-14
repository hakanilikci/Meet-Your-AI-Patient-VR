using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Management;

// Project setup and builds from the command line (also from the menu "MedicalVR/...").
//   Unity -batchmode -projectPath ui/MedicalVR -executeMethod Setup.Configure -quit   (settings + scene, once)
//   Unity -batchmode -projectPath ui/MedicalVR -executeMethod Setup.BuildSimulator -quit  → ui/MedicalVR/Build/visionOS
public static class Setup {
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("MedicalVR/Configure project")]
    public static void Configure() {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.VisionOS)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.VisionOS, BuildTarget.VisionOS);

        // XR: Apple visionOS loader, full VR (Metal rendering, the room replaces the real world; hands stay visible).
        Directory.CreateDirectory("Assets/XR/Settings");
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget)) {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.VisionOS))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.VisionOS);
        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.VisionOS);
        general.InitManagerOnStart = true;
        if (general.AssignedSettings == null) {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = "VisionOS Providers";
            AssetDatabase.AddObjectToAsset(manager, AssetDatabase.GetAssetPath(perTarget));
            general.AssignedSettings = manager;
            EditorUtility.SetDirty(general);
        }
        XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.VisionOS.VisionOSLoader", BuildTargetGroup.VisionOS);

        var vos = VisionOSSettings.currentSettings;
        if (vos == null) {
            vos = ScriptableObject.CreateInstance<VisionOSSettings>();
            AssetDatabase.CreateAsset(vos, "Assets/XR/Settings/VisionOSSettings.asset");
            VisionOSSettings.currentSettings = vos;
        }
        vos.appMode = VisionOSSettings.AppMode.Metal;
        vos.metalImmersionStyle = VisionOSSettings.ImmersionStyle.Full;
        vos.upperLimbVisibility = VisionOSSettings.Visibility.Visible;
        // no hand-joint tracking (look + pinch needs none); on at startup it needs a permission text or the build errs
        var runtime = vos.GetOrCreateRuntimeSettings();
        if (runtime != null) { runtime.initializeHandTrackingOnStartup = false; EditorUtility.SetDirty(runtime); }
        EditorUtility.SetDirty(vos);

        // Player: names, local http to the ai laptop, microphone text, simulator SDK by default.
        PlayerSettings.companyName = "MedicalVR";
        PlayerSettings.productName = "Meet Your AI Patient";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.VisionOS, "de.medicalvr.aipatient");
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        PlayerSettings.iOS.microphoneUsageDescription = "You talk to the AI patient with your voice.";
        PlayerSettings.VisionOS.sdkVersion = VisionOSSdkVersion.Simulator;
        PlayerSettings.VisionOS.targetOSVersionString = "2.0";   // Metal apps need visionOS 2
        PlayerSettings.colorSpace = ColorSpace.Linear;             // required for Metal rendering on visionOS
        PlayerSettings.SplashScreen.show = false;
        var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        ps.FindProperty("activeInputHandler").intValue = 2;   // both: Input System for PolySpatial, legacy for TMP
        ps.ApplyModifiedProperties();

        // TextMeshPro default font and shaders.
        if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) {
            var pkg = Directory.GetFiles("Library/PackageCache", "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).First();
            AssetDatabase.ImportPackage(pkg, false);
        }

        // PolySpatial stays installed (switch appMode back to RealityKit for mixed reality); with Metal it is off.
        const string polyPath = "Assets/Resources/PolySpatialSettings.asset";
        if (AssetDatabase.LoadAssetAtPath<Unity.PolySpatial.PolySpatialSettings>(polyPath) == null) {
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Unity.PolySpatial.PolySpatialSettings>(), polyPath);
        }

        SkipPolySpatialLayout();
        MakePatientMaterial();
        MakeGlowMaterial();
        RoomImport.Import();     // Poly Haven art, if ui/tools/fetch_room_assets.py fetched it
        SetupSpeechPlugin();
        AvatarImport.Import();   // Hannah from avatar/generated_avatar, if it is there
        RocketboxImport.Import(); // patient_male_older (Male_Adult_05), if ui/tools/fetch_rocketbox.py fetched it
        MakeScene();
        AssetDatabase.SaveAssets();
        Debug.Log("SETUP done");
    }

    // PolySpatial 3.3.1 bug: it writes "layout loaded" to SessionState but reads EditorPrefs, so it reloads
    // its layout on every editor start, which fails with "Missing Default Layout". Set the pref it reads.
    // Run once per Mac: Unity -batchmode -projectPath ui/MedicalVR -executeMethod Setup.SkipPolySpatialLayout -quit
    public static void SkipPolySpatialLayout() => EditorPrefs.SetBool("PolySpatial/LayoutLoaded", true);

    // Runtime-made primitives show magenta on visionOS (their default material's shader is stripped).
    // A material asset in Resources keeps the Standard shader in the build; PlaceholderPatient copies it.
    public static void MakePatientMaterial() {
        const string path = "Assets/Resources/PatientMaterial.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Directory.CreateDirectory("Assets/Resources");
        AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), path);
        AssetDatabase.SaveAssets();
    }

    // Emissive primitives (window, screen, lamps) copy this, so the build keeps the Standard shader's emission variant.
    public static void MakeGlowMaterial() {
        const string path = "Assets/Resources/GlowMaterial.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var m = new Material(Shader.Find("Standard"));
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
    }

    /// SpeechBridge.mm is native code for the headset only.
    public static void SetupSpeechPlugin() {
        const string plugin = "Assets/Plugins/visionOS/SpeechBridge.mm";
        AssetDatabase.ImportAsset(plugin, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(plugin);
        Debug.Log($"SETUP speech plugin importer: {(importer == null ? "none" : importer.GetType().Name)}");
        if (importer is not PluginImporter pi) return;
        pi.SetCompatibleWithAnyPlatform(false);
        pi.SetCompatibleWithEditor(false);
        pi.SetCompatibleWithPlatform(BuildTarget.VisionOS, true);
        pi.SaveAndReimport();
        Debug.Log($"SETUP speech plugin for visionOS: {pi.GetCompatibleWithPlatform(BuildTarget.VisionOS)}");
    }

    /// The headset reaches ai on the Mac that builds the app. All of the Mac's IPv4 addresses go into the app, one per
    /// line, the Internet Sharing bridge (the headset on the Mac's own Wi-Fi) first; Connect tries them in turn.
    static void WriteServerAddress() {
        var urls = new System.Collections.Generic.List<string>();
        foreach (var nic in new[] { "bridge100", "en0", "en5", "en1", "en6" }) {
            try {
                var psi = new System.Diagnostics.ProcessStartInfo("/sbin/ifconfig", nic) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using var pr = System.Diagnostics.Process.Start(psi);
                var text = pr.StandardOutput.ReadToEnd(); pr.WaitForExit();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"inet (\d+\.\d+\.\d+\.\d+)"))
                    if (!m.Groups[1].Value.StartsWith("127.")) urls.Add($"http://{m.Groups[1].Value}:8000");
            } catch { }
        }
        if (urls.Count == 0) urls.Add("http://127.0.0.1:8000");
        Directory.CreateDirectory("Assets/Resources");
        File.WriteAllText("Assets/Resources/server.txt", string.Join("\n", urls.Distinct()));
        AssetDatabase.ImportAsset("Assets/Resources/server.txt");
        Debug.Log("SETUP server addresses " + string.Join(", ", urls.Distinct()));
    }

    /// Renders the room with the patient from the student's eyes to Build/room_check.png (no headset needed).
    public static void RenderRoomCheck() {
        var room = Room.Build();
        var patient = App.CreatePatient(new Vector3(0, 0, 1.6f), App.Characters[0].id);
        if (patient is Component pc) room.Light(pc.gameObject);
        if (patient is AvatarPatient ap) { ap.StudentEyes = new Vector3(0, 1.6f, 0); ap.PoseForEditor(0.5f, 0f); }
        var doctor = App.CreateDoctor(App.DoctorPosition);   // as during the feedback
        if (doctor != null) { room.Light(doctor.gameObject); doctor.StudentEyes = new Vector3(0, 1.6f, 0); doctor.PoseForEditor(1f, 0f); }
        foreach (var probe in room.GetComponentsInChildren<ReflectionProbe>()) probe.RenderProbe();
        foreach (var (name, pos, look, fov) in new[] {
            ("room_check_student", new Vector3(0, 1.55f, 0), new Vector3(0, 1.0f, 1.6f), 70f),
            ("room_check_wide", new Vector3(-1.6f, 1.9f, -1.5f), new Vector3(0.3f, 0.9f, 1.6f), 70f) }) {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(1280, 800, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply();
            Directory.CreateDirectory("Build"); File.WriteAllBytes($"Build/{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null; Object.DestroyImmediate(cam.gameObject);
        }
        Debug.Log("SETUP room render check: Build/room_check_student.png, Build/room_check_wide.png");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// Every character seated in the room, seen from the student's side: Build/char_<id>.png.
    public static void RenderCharactersCheck() {
        var room = Room.Build();
        foreach (var r in room.GetComponentsInChildren<ReflectionProbe>()) r.RenderProbe();
        foreach (var (id, _) in App.Characters) {
            if (Resources.Load<GameObject>(id) == null) { Debug.Log("SETUP character missing: " + id); continue; }
            var patient = App.CreatePatient(new Vector3(0, 0, 1.6f), id);
            var go = ((Component)patient).gameObject;
            if (patient is AvatarPatient ap) { ap.StudentEyes = new Vector3(0, 1.6f, 0); ap.PoseForEditor(2f, 0f); }
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = new Vector3(-0.5f, 1.45f, 0.1f); cam.transform.LookAt(new Vector3(0, 0.75f, 1.35f)); cam.fieldOfView = 55;
            var rt = new RenderTexture(480, 640, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(480, 640, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 480, 640), 0, 0); tex.Apply();
            Directory.CreateDirectory("Build/chars"); File.WriteAllBytes($"Build/chars/{id}.png", tex.EncodeToPNG());
            RenderTexture.active = null; Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(go);
            Debug.Log("SETUP character rendered: " + id);
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// The student's view at the feedback: panel with Restart / Quit above it, the app's keyboard open, the patient
    /// seated and the doctor standing by the desk. Build/ui_check.png
    public static void RenderUICheck() {
        PlayerPrefs.DeleteKey("character_id");
        var app = new GameObject("App").AddComponent<App>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(App).GetMethod("Start", flags).Invoke(app, null);
        typeof(App).GetMethod("ShowDoctor", flags).Invoke(app, new object[] { true });
        VirtualKeyboard.Instance.Open((TMPro.TMP_InputField)typeof(App).GetField("urlField", flags).GetValue(app));
        foreach (var name in new[] { "patient", "doctor" }) {
            var v = typeof(App).GetField(name, flags).GetValue(app);
            var ap = v is GameObject g ? g.GetComponent<AvatarPatient>() : v as AvatarPatient;
            if (ap != null) { ap.StudentEyes = new Vector3(0, 1.6f, 0); ap.PoseForEditor(1f, 0f); }
        }
        Canvas.ForceUpdateCanvases();
        foreach (var r in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) r.RenderProbe();
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0.05f, 1.6f, -0.1f); cam.transform.LookAt(new Vector3(-0.1f, 1.15f, 1.3f));
        cam.fieldOfView = 80; cam.nearClipPlane = 0.05f;
        var rt = new RenderTexture(1440, 900, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1440, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); tex.Apply();
        Directory.CreateDirectory("Build"); File.WriteAllBytes("Build/ui_check.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("SETUP ui render check: Build/ui_check.png");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void MakeScene() {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // Full VR: XR Origin at the student's feet, the head-tracked camera under it, and the AR session that
        // visionOS needs for head tracking in Metal mode.
        Object.DestroyImmediate(GameObject.Find("Main Camera"));
        Object.DestroyImmediate(GameObject.Find("Directional Light"));   // Room makes its own lights
        var origin = new GameObject("XR Origin").AddComponent<Unity.XR.CoreUtils.XROrigin>();
        var offset = new GameObject("Camera Offset").transform; offset.SetParent(origin.transform, false);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(offset, false);
        camGo.transform.localPosition = new Vector3(0, 1.6f, 0);   // editor only; the headset sets the real pose
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f; cam.allowHDR = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<AudioListener>();
        var tpd = camGo.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction("Position", binding: "<XRHMD>/devicePosition"));
        tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<XRHMD>/deviceRotation"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction("Tracking State", binding: "<XRHMD>/trackingState"));
        origin.Camera = cam;
        origin.CameraFloorOffsetObject = offset.gameObject;
        new GameObject("AR Session").AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
        new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        new GameObject("App").AddComponent<App>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    [MenuItem("MedicalVR/Build for visionOS simulator")]
    public static void BuildSimulator() => Build(VisionOSSdkVersion.Simulator);

    [MenuItem("MedicalVR/Build for Vision Pro (device)")]
    public static void BuildDevice() => Build(VisionOSSdkVersion.Device);

    static void Build(VisionOSSdkVersion sdk) {
        SetupSpeechPlugin();
        WriteServerAddress();
        PlayerSettings.VisionOS.sdkVersion = sdk;
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ScenePath }, target = BuildTarget.VisionOS, locationPathName = "Build/visionOS" });
        Debug.Log($"BUILD {r.summary.result} errors={r.summary.totalErrors} → Build/visionOS");
        if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    // ai is on the local network: the headset asks once (ai/CONTRACT.md).
    [PostProcessBuild]
    public static void AddLocalNetwork(BuildTarget target, string path) {
        if (target != BuildTarget.VisionOS) return;
        var plist = Path.Combine(path, "Info.plist");
        if (!File.Exists(plist)) return;
        var s = File.ReadAllText(plist);
        if (s.Contains("NSLocalNetworkUsageDescription")) return;
        var add = "\t<key>NSLocalNetworkUsageDescription</key>\n\t<string>Connects to the AI patient server on this network.</string>\n" +
                  "\t<key>NSMicrophoneUsageDescription</key>\n\t<string>You talk to the AI patient with your voice.</string>\n" +
                  "\t<key>NSSpeechRecognitionUsageDescription</key>\n\t<string>Turns what you say into text for the AI patient.</string>\n";
        var i = s.LastIndexOf("</dict>");
        File.WriteAllText(plist, s.Insert(i, add));

        // on-device speech-to-text (Plugins/visionOS/SpeechBridge.mm) needs Speech.framework
        var pbx = Path.Combine(path, "Unity-VisionOS.xcodeproj/project.pbxproj");
        var proj = new UnityEditor.iOS.Xcode.PBXProject();
        proj.ReadFromFile(pbx);
        proj.AddFrameworkToProject(proj.GetUnityFrameworkTargetGuid(), "Speech.framework", false);
        proj.WriteToFile(pbx);
    }
}
