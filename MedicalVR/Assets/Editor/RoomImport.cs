using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Turns the Poly Haven art (CC0) fetched by ui/tools/fetch_room_assets.py into prefabs and materials Room.cs loads:
// Resources/RoomArt/<model id>.prefab, RoomArt/floor.mat, RoomArt/wall.mat, RoomArt/sky.mat. Nothing here goes to git.
//   python3 ui/tools/fetch_room_assets.py
//   Unity -batchmode -projectPath ui/MedicalVR -executeMethod RoomImport.Run
public static class RoomImport {
    const string Root = "Assets/Room/PolyHaven";
    const string Res = Root + "/Resources/RoomArt";
    const string Mats = Root + "/Materials";

    public static void Run() { Import(); if (Application.isBatchMode) EditorApplication.Exit(0); }

    [MenuItem("MedicalVR/Import room art (Poly Haven)")]
    public static void Import() {
        if (!Directory.Exists(Root)) { Debug.LogWarning("ROOM art not found, run ui/tools/fetch_room_assets.py; the room uses simple shapes."); return; }
        Directory.CreateDirectory(Res); Directory.CreateDirectory(Mats);
        AssetDatabase.Refresh();
        foreach (var dir in Directory.GetDirectories(Root)) {
            var id = Path.GetFileName(dir);
            var fbx = $"{dir}/{id}.fbx";
            if (File.Exists(fbx)) Model(id, fbx.Replace('\\', '/'));
        }
        Surface("floor", "laminate_floor_02", new Vector2(4, 4), Color.white, 0.35f);
        // walls: painted (own colour), the plaster texture only for its relief (normal map)
        Surface("wall", "beige_wall_001", new Vector2(3, 2), new Color(0.93f, 0.94f, 0.91f), 0.15f, albedo: false);
        Surface("wall_band", "beige_wall_001", new Vector2(3, 0.6f), new Color(0.60f, 0.76f, 0.74f), 0.3f, albedo: false);
        Sky();
        AssetDatabase.SaveAssets();
        Debug.Log("ROOM art imported into " + Res);
    }

    static void Model(string id, string fbx) {
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
        if (mi == null) { Debug.LogWarning("ROOM no importer for " + fbx); return; }
        mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
        // Room.OnShelf rays onto the shelves' mesh at run time: on the headset only readable meshes take a collider
        mi.isReadable = id == "steel_frame_shelves_01";
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.SaveAndReimport();
        var sets = TextureSets(Path.GetDirectoryName(fbx) + "/textures");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        go.name = id;
        var made = new Dictionary<string, Material>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) {
            r.sharedMaterials = r.sharedMaterials.Select(m => {
                var name = m != null ? m.name : id;
                if (made.TryGetValue(name, out var done)) return done;
                if (name.ToLower().Contains("glass")) return made[name] = Glass($"{id}__{name}");
                var set = Match(name, sets);
                return made[name] = MakeMaterial($"{id}__{name}", set, Color.white, Vector2.one, 0.3f);
            }).ToArray();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        var b = Bounds(go);
        PrefabUtility.SaveAsPrefabAsset(go, $"{Res}/{id}.prefab");
        Object.DestroyImmediate(go);
        Debug.Log($"ROOM model {id}: size {b.size.ToString("F2")}, centre {b.center.ToString("F2")}, materials {string.Join(", ", made.Keys)}");
    }

    /// Texture sets in a folder, by prefix: "potted_plant_02_pot" → {diff, nor_gl, rough, metal, arm, alpha}.
    static Dictionary<string, Dictionary<string, string>> TextureSets(string dir) {
        var sets = new Dictionary<string, Dictionary<string, string>>();
        if (!Directory.Exists(dir)) return sets;
        foreach (var f in Directory.GetFiles(dir).Where(f => f.EndsWith(".jpg") || f.EndsWith(".png"))) {
            var n = Path.GetFileNameWithoutExtension(f);
            if (n.EndsWith("_ms") || n.EndsWith("_diffa")) continue;   // made by this importer
            foreach (var map in new[] { "nor_gl", "diff", "rough", "metal", "arm", "alpha" }) {
                var i = n.IndexOf("_" + map + "_");
                if (i < 0) continue;
                var prefix = n.Substring(0, i);
                if (!sets.TryGetValue(prefix, out var s)) sets[prefix] = s = new Dictionary<string, string>();
                s[map] = f.Replace('\\', '/');
                break;
            }
        }
        return sets;
    }

    static Dictionary<string, string> Match(string material, Dictionary<string, Dictionary<string, string>> sets) {
        if (sets.Count == 0) return null;
        if (sets.Count == 1) return sets.Values.First();
        var m = material.ToLower();
        var best = sets.Keys.OrderByDescending(p => m.Contains(p.ToLower()) ? p.Length : p.ToLower().Split('_').Count(w => m.Contains(w))).First();
        return sets[best];
    }

    static void Surface(string name, string id, Vector2 tiling, Color tint, float smooth, bool albedo = true) {
        var sets = TextureSets($"{Root}/{id}/textures");
        if (sets.Count == 0) return;
        var set = new Dictionary<string, string>(sets.Values.First());
        if (!albedo) { set.Remove("diff"); set.Remove("arm"); set.Remove("rough"); }
        var mat = MakeMaterial(name, set, tint, tiling, smooth);
        var path = $"{Res}/{name}.mat";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(Object.Instantiate(mat), path);
    }

    /// Standard material: albedo (with alpha cut-out where the model has an alpha map), normal map, and
    /// metallic (R) + smoothness (A) packed from the roughness / metal / ARM maps.
    static Material MakeMaterial(string name, Dictionary<string, string> set, Color tint, Vector2 tiling, float smooth) {
        var mat = new Material(Shader.Find("Standard")) { name = name, color = tint };
        mat.SetFloat("_Glossiness", smooth);
        mat.SetFloat("_Metallic", 0f);
        if (set != null) {
            string Get(string k) => set.TryGetValue(k, out var p) ? p : null;
            var diff = Get("diff");
            if (diff != null && Get("alpha") != null) diff = Compose(diff, Get("alpha"), "_diffa", (c, a) => new Color32(c.r, c.g, c.b, a.r), true);
            if (diff != null) { mat.mainTexture = Tex(diff, true); mat.mainTextureScale = tiling; }
            if (Get("alpha") != null) {
                mat.SetFloat("_Mode", 1); mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON"); mat.SetOverrideTag("RenderType", "TransparentCutout"); mat.renderQueue = 2450;
            }
            var nor = Get("nor_gl");
            if (nor != null) {
                var ti = (TextureImporter)AssetImporter.GetAtPath(nor);
                if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nor));
                mat.SetTextureScale("_BumpMap", tiling);
                mat.EnableKeyword("_NORMALMAP");
            }
            string ms = null;
            if (Get("arm") != null) ms = Compose(Get("arm"), null, "_ms", (c, _) => new Color32(c.b, 0, 0, (byte)(255 - c.g)), false);
            else if (Get("rough") != null) ms = Compose(Get("rough"), Get("metal"), "_ms", (r, m) => new Color32(m.r, 0, 0, (byte)(255 - r.r)), false);
            if (ms != null) {
                mat.SetTexture("_MetallicGlossMap", Tex(ms, false));
                mat.SetFloat("_GlossMapScale", 0.85f);
                mat.EnableKeyword("_METALLICGLOSSMAP");
            }
        }
        var path = $"{Mats}/{name}.mat";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// Clear glass (clock face cover, picture glass): transparent, shiny.
    static Material Glass(string name) {
        var mat = new Material(Shader.Find("Standard")) { name = name, color = new Color(1, 1, 1, 0.08f) };
        mat.SetFloat("_Mode", 3); mat.SetFloat("_Glossiness", 0.95f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0); mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
        var path = $"{Mats}/{name}.mat";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// Writes "<a>-prefix><suffix>.png" from map a (and b, if any) pixel by pixel.
    static string Compose(string a, string b, string suffix, System.Func<Color32, Color32, Color32> f, bool srgb) {
        var n = Path.GetFileNameWithoutExtension(a);
        var cut = n.LastIndexOf('_', n.LastIndexOf('_') - 1);   // drop "_diff_1k"
        var outPath = $"{Path.GetDirectoryName(a)}/{n.Substring(0, cut)}{suffix}.png".Replace('\\', '/');
        if (!File.Exists(outPath)) {
            var ta = Load(a); var pa = ta.GetPixels32();
            Color32[] pb = null;
            if (b != null) { var tb = Load(b); if (tb.width != ta.width || tb.height != ta.height) tb = Resize(tb, ta.width, ta.height); pb = tb.GetPixels32(); }
            var po = new Color32[pa.Length];
            for (int i = 0; i < pa.Length; i++) po[i] = f(pa[i], pb != null ? pb[i] : new Color32(0, 0, 0, 255));
            var t = new Texture2D(ta.width, ta.height, TextureFormat.RGBA32, false); t.SetPixels32(po); t.Apply();
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        if (ti.sRGBTexture != srgb || (srgb && !ti.alphaIsTransparency)) { ti.sRGBTexture = srgb; ti.alphaIsTransparency = srgb; ti.SaveAndReimport(); }
        return outPath;
    }

    static Texture2D Load(string path) { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path)); return t; }

    static Texture2D Resize(Texture2D src, int w, int h) {
        var rt = RenderTexture.GetTemporary(w, h); Graphics.Blit(src, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return t;
    }

    static Texture2D Tex(string path, bool srgb) {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti != null && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Sky() {
        var hdr = Directory.Exists(Root + "/hdri") ? Directory.GetFiles(Root + "/hdri", "*.hdr").FirstOrDefault() : null;
        if (hdr == null) return;
        hdr = hdr.Replace('\\', '/');
        var ti = (TextureImporter)AssetImporter.GetAtPath(hdr);
        if (ti.textureShape != TextureImporterShape.TextureCube) { ti.textureShape = TextureImporterShape.TextureCube; ti.SaveAndReimport(); }
        var mat = new Material(Shader.Find("Skybox/Cubemap"));
        mat.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(hdr));
        mat.SetFloat("_Exposure", 1.0f);
        var path = $"{Res}/sky.mat";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mat, path);
    }

    static Bounds Bounds(GameObject go) {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
