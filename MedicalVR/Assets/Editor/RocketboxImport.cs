using System.IO;
using System.Linq;
using MedicalVR.PatientAvatar;
using UnityEditor;
using UnityEngine;

// Brings avatar's characters (avatar/avatar_show: Microsoft Rocketbox models, MIT; ui/tools/fetch_rocketbox.py) into
// the project: each model with its textures, Rocketbox's seated clips as legacy clips (the headset samples them
// without an Animator), and a prefab Resources/<avatar name> with avatar's PatientAvatarController and ui's AvatarPatient.
//   Unity -batchmode -projectPath ui/MedicalVR -executeMethod RocketboxImport.Run
public static class RocketboxImport {
    const string Root = "Assets/Avatar/Rocketbox";
    const string Res = Root + "/Resources";
    // avatar's logical name -> Rocketbox model (same list as ui/tools/fetch_rocketbox.py and avatar/avatar_show)
    public static readonly (string id, string model)[] Characters = {
        ("patient_male_older", "Male_Adult_05"), ("patient_male_older_2", "Male_Adult_14"),
        ("patient_male_south_asian", "Male_Adult_17"), ("patient_male_young", "Male_Adult_06"),
        ("patient_female_older", "Female_Adult_09"), ("patient_female_elderly", "Female_Adult_02"),
        ("patient_female_young", "Female_Adult_17"), ("patient_female_young_2", "Female_Adult_15"),
        ("attending", "Medical_Male_03"),
    };
    const string Idle = "sit_chair_idle_neutral_01", Talk = "sit_chair_gestic_thoughtful";

    public static void Run() { Import(); if (Application.isBatchMode) EditorApplication.Exit(0); }

    [MenuItem("MedicalVR/Import Rocketbox characters")]
    public static void Import() {
        if (!Directory.Exists(Root)) { Debug.LogWarning("ROCKETBOX not found, run ui/tools/fetch_rocketbox.py"); return; }
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Res); Directory.CreateDirectory(Root + "/Materials");
        foreach (var (id, model) in Characters) {
            try { One(id, model); } catch (System.Exception e) { Debug.LogError($"ROCKETBOX {id} failed: {e.Message}"); }
        }
        AssetDatabase.SaveAssets();
    }

    static AnimationClip Clip(string name) {
        var path = $"{Root}/Animations/{name}.fbx";
        var ai = (ModelImporter)AssetImporter.GetAtPath(path);
        if (ai == null) return null;
        if (ai.animationType != ModelImporterAnimationType.Legacy || !ai.importAnimation) {
            ai.animationType = ModelImporterAnimationType.Legacy;
            ai.importAnimation = true; ai.materialImportMode = ModelImporterMaterialImportMode.None;
            ai.animationCompression = ModelImporterAnimationCompression.Off;
            var takes = ai.defaultClipAnimations;
            foreach (var t in takes) { t.wrapMode = WrapMode.Loop; t.loopTime = true; }
            ai.clipAnimations = takes;
            ai.SaveAndReimport();
        }
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }

    static void One(string id, string model) {
        var fbx = $"{Root}/{model}/{model}.fbx";
        if (!File.Exists(fbx)) { Debug.LogWarning($"ROCKETBOX {model} not downloaded"); return; }
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
        mi.animationType = ModelImporterAnimationType.Legacy;
        mi.importAnimation = false;
        mi.importBlendShapes = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.SaveAndReimport();

        var sex = model.StartsWith("Female") ? "f" : "m";
        // the attending stands by the desk during the feedback; the patients sit
        bool standing = id == "attending";
        var idle = standing ? Clip("m_idle_neutral_01") : Clip($"{sex}_{Idle}");
        var talk = standing ? Clip("m_gestic_talk_neutral_01") : Clip($"{sex}_{Talk}");

        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        root.name = id;
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(root.GetComponent<Animation>());

        // materials: the FBX material "m009_head" uses Textures/m009_head_color.tga (+ _normal); "_opacity" (hair,
        // lashes, brows) is cut out. Textures at 1024 px (2048 for the face), so nine characters fit in the app.
        Texture2D T(string n, bool normal, int size) {
            var p = $"{Root}/{model}/Textures/{n}.tga";
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (ti == null) return null;
            bool alpha = n.Contains("opacity");
            if ((normal && ti.textureType != TextureImporterType.NormalMap) || ti.maxTextureSize != size || ti.alphaIsTransparency != alpha) {
                if (normal) ti.textureType = TextureImporterType.NormalMap;
                ti.maxTextureSize = size; ti.alphaIsTransparency = alpha;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
            r.sharedMaterials = r.sharedMaterials.Select(m => {
                var n = (m != null ? m.name : r.name).ToLower().Replace(" (instance)", "");
                var mat = new Material(Shader.Find("Standard")) { name = $"{id}_{n}" };
                bool head = n.Contains("head"), cut = n.Contains("opacity");
                mat.mainTexture = T($"{n}_color", false, head ? 2048 : 1024);
                if (mat.mainTexture == null) Debug.LogWarning($"ROCKETBOX {id}: no texture for material {n}");
                mat.SetFloat("_Glossiness", head ? 0.35f : 0.2f);
                var nor = cut ? null : T($"{n}_normal", true, 1024);
                if (nor != null) { mat.SetTexture("_BumpMap", nor); mat.EnableKeyword("_NORMALMAP"); }
                if (cut) {
                    mat.SetFloat("_Mode", 1); mat.SetFloat("_Cutoff", 0.4f);
                    mat.EnableKeyword("_ALPHATEST_ON"); mat.SetOverrideTag("RenderType", "TransparentCutout"); mat.renderQueue = 2450;
                }
                var path = $"{Root}/Materials/{mat.name}.mat";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mat, path);
                return mat;
            }).ToArray();
        }

        var faces = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0).ToArray();
        foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.updateWhenOffscreen = true;

        var voice = root.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 1f; voice.minDistance = 1f;
        var ctl = root.AddComponent<PatientAvatarController>();
        var so = new SerializedObject(ctl);
        so.FindProperty("animator").objectReferenceValue = null;
        so.FindProperty("speechAudio").objectReferenceValue = voice;
        so.FindProperty("head").objectReferenceValue = null;
        var arr = so.FindProperty("faceMeshes"); arr.arraySize = faces.Length;
        for (int i = 0; i < faces.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = faces[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        var adapter = root.AddComponent<AvatarPatient>();
        var aso = new SerializedObject(adapter);
        aso.FindProperty("controller").objectReferenceValue = ctl;
        aso.FindProperty("voice").objectReferenceValue = voice;
        aso.FindProperty("sitClip").objectReferenceValue = idle;
        aso.FindProperty("talkClip").objectReferenceValue = talk;
        var farr = aso.FindProperty("faces"); farr.arraySize = faces.Length;
        for (int i = 0; i < faces.Length; i++) farr.GetArrayElementAtIndex(i).objectReferenceValue = faces[i];
        aso.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, $"{Res}/{id}.prefab");
        Object.DestroyImmediate(root);
        Debug.Log($"ROCKETBOX {id} ({model}) ready: clips {(idle ? idle.name : "NONE")} / {(talk ? talk.name : "NONE")}, {faces.Length} face meshes");
    }
}
