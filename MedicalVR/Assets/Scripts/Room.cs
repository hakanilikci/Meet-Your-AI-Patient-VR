using UnityEngine;
using UnityEngine.Rendering;

/// The GP consultation room around the student (full VR): textured floor and walls, a window, the doctor's desk
/// with a monitor, a plant and a vase of flowers, shelves, a cabinet, plants, a clock, a picture, an examination couch.
/// Furniture and surfaces are Poly Haven models (CC0, ui/tools/fetch_room_assets.py + Editor/RoomImport.cs, loaded
/// from Resources/RoomArt); without them the same room is built from simple shapes, so the app always runs.
/// Coordinates: the student stands at the origin facing +z; the patient sits about 1.2 m ahead.
public class Room : MonoBehaviour {
    public const float W = 4.8f, Front = -1.8f, Back = 3.4f, H = 2.8f;

    public static Room Build() {
        var room = new GameObject("Room").AddComponent<Room>();
        room.Make();
        return room;
    }

    /// Puts an object (the patient) into the room.
    public void Light(GameObject go) => go.transform.SetParent(transform, true);

    /// The patient's armchair, local to the patient (facing +z), seat top at `seat`, seat centre at `seatZ`.
    public static void PatientChair(Transform patient, float seat, float seatZ) {
        // modern_arm_chair_01: seat cushion top 0.44 m, seat centre 0.06 m in front of the model origin
        const float ModelSeat = 0.44f, ModelSeatZ = 0.06f;
        var s = seat / ModelSeat;
        if (Art("modern_arm_chair_01", new Vector3(0, 0, seatZ - ModelSeatZ * s), 0, patient, s) == null)
            PlaceholderPatient.MakeChair(patient, seat, seatZ);
    }

    void Make() {
        current = transform;
        Lighting();
        var floorArt = Resources.Load<Material>("RoomArt/floor");
        var wallArt = Resources.Load<Material>("RoomArt/wall");
        var bandArt = Resources.Load<Material>("RoomArt/wall_band");
        var floor = floorArt ?? C(0.78f, 0.76f, 0.72f); var wall = wallArt ?? C(0.86f, 0.91f, 0.88f); var wallLow = bandArt ?? C(0.62f, 0.76f, 0.72f);
        var white = C(0.95f, 0.95f, 0.94f); var dark = C(0.12f, 0.13f, 0.15f);
        var metal = C(0.72f, 0.74f, 0.77f, 0.6f); var cream = C(0.93f, 0.90f, 0.82f);
        float cx = 0f, cz = (Front + Back) / 2, D = Back - Front;

        // floor, ceiling, walls (with a coloured band at the bottom, like hospital wall protection)
        Box("Floor", new Vector3(cx, -0.01f, cz), new Vector3(W, 0.02f, D), floor);
        Box("Ceiling", new Vector3(cx, H, cz), new Vector3(W, 0.02f, D), white);
        foreach (var (name, pos, size) in new[] {
            ("Wall back", new Vector3(cx, H / 2, Back), new Vector3(W, H, 0.05f)),
            ("Wall front", new Vector3(cx, H / 2, Front), new Vector3(W, H, 0.05f)),
            ("Wall left", new Vector3(-W / 2, H / 2, cz), new Vector3(0.05f, H, D)),
            ("Wall right", new Vector3(W / 2, H / 2, cz), new Vector3(0.05f, H, D)) }) {
            Box(name, pos, size, wall);
            var low = size; low.y = 0.9f;
            Box(name + " band", new Vector3(pos.x, 0.45f, pos.z) - Inward(pos) * 0.01f, low + Thick(size) * 0.01f, wallLow);
            Box(name + " skirting", new Vector3(pos.x, 0.05f, pos.z) - Inward(pos) * 0.02f, new Vector3(low.x, 0.1f, low.z) + Thick(size) * 0.02f, white);
        }

        // window behind the patient: bright sky, white frame, sill, a vase of flowers on the sill
        Box("Window sky", new Vector3(-0.2f, 1.55f, Back - 0.03f), new Vector3(1.8f, 1.2f, 0.01f), Glow(new Color(0.62f, 0.78f, 0.95f), 0.9f));
        foreach (var (p, s) in new[] {
            (new Vector3(-0.2f, 2.17f, Back - 0.05f), new Vector3(1.9f, 0.06f, 0.06f)), (new Vector3(-0.2f, 0.93f, Back - 0.05f), new Vector3(1.9f, 0.06f, 0.06f)),
            (new Vector3(-1.13f, 1.55f, Back - 0.05f), new Vector3(0.06f, 1.3f, 0.06f)), (new Vector3(0.73f, 1.55f, Back - 0.05f), new Vector3(0.06f, 1.3f, 0.06f)),
            (new Vector3(-0.2f, 1.55f, Back - 0.05f), new Vector3(0.04f, 1.2f, 0.05f)) })
            Box("Window frame", p, s, white);
        Box("Window sill", new Vector3(-0.2f, 0.9f, Back - 0.12f), new Vector3(2.0f, 0.04f, 0.22f), white);
        Flowers(new Vector3(-0.85f, 0.92f, Back - 0.12f), 0.8f);

        // the doctor's desk, to the student's right: monitor, keyboard, folder, a small plant, a vase of flowers
        var desk = new GameObject("Desk").transform; desk.SetParent(transform, false);
        desk.localPosition = new Vector3(1.45f, 0, 0.7f); desk.localRotation = Quaternion.Euler(0, -90, 0);   // its length runs along z
        float top = 0.79f;
        if (Art("metal_office_desk", Vector3.zero, 0, desk) == null) {
            top = 0.76f;
            Box("Desk top", new Vector3(0, 0.74f, 0), new Vector3(1.5f, 0.04f, 0.75f), C(0.62f, 0.45f, 0.30f), desk);
            Box("Desk drawers", new Vector3(0.55f, 0.36f, 0), new Vector3(0.4f, 0.72f, 0.7f), C(0.62f, 0.45f, 0.30f), desk);
        }
        // desk-local: x along the desk (−x = towards the student), z = depth (+z = the student's side)
        Box("Monitor", new Vector3(0.1f, top + 0.3f, -0.15f), new Vector3(0.58f, 0.36f, 0.03f), dark, desk);
        Box("Monitor screen", new Vector3(0.1f, top + 0.3f, -0.133f), new Vector3(0.54f, 0.32f, 0.005f), Glow(new Color(0.55f, 0.72f, 0.85f), 0.8f), desk);
        Box("Monitor stand", new Vector3(0.1f, top + 0.06f, -0.17f), new Vector3(0.06f, 0.12f, 0.04f), dark, desk);
        Box("Monitor foot", new Vector3(0.1f, top + 0.005f, -0.16f), new Vector3(0.22f, 0.01f, 0.18f), dark, desk);
        Box("Keyboard", new Vector3(0.1f, top + 0.012f, 0.15f), new Vector3(0.44f, 0.02f, 0.15f), dark, desk);
        Box("Mouse", new Vector3(0.42f, top + 0.015f, 0.17f), new Vector3(0.06f, 0.03f, 0.1f), dark, desk);
        if (Art("binder_notebook", new Vector3(-0.45f, top, 0.15f), 15, desk) == null)
            Box("Papers", new Vector3(-0.45f, top + 0.005f, 0.15f), new Vector3(0.3f, 0.01f, 0.22f), white, desk);
        Art("book_encyclopedia_set_01", new Vector3(0.62f, top, -0.3f), 0, desk, 0.7f);
        Art("potted_plant_04", new Vector3(-0.75f, top, -0.25f), 0, desk);
        Flowers(desk.TransformPoint(new Vector3(-0.35f, top, -0.28f)), 1f);
        // the doctor's chair, on the far side of the desk
        if (Art("dining_chair_02", new Vector3(2.1f, 0, 0.6f), -90) == null) {
            var dchair = new GameObject("Doctor chair").transform; dchair.SetParent(transform, false);
            dchair.localPosition = new Vector3(2.1f, 0, 0.6f); dchair.localRotation = Quaternion.Euler(0, -90, 0);
            PlaceholderPatient.MakeChair(dchair, 0.48f, 0);
        }

        // examination couch along the left wall, with a paper sheet and a pillow, and a curtain rail
        var couch = new GameObject("Couch").transform; couch.SetParent(transform, false);
        couch.localPosition = new Vector3(-1.95f, 0, 1.9f);
        Box("Couch top", new Vector3(0, 0.62f, 0), new Vector3(0.68f, 0.12f, 1.9f), C(0.20f, 0.38f, 0.55f, 0.4f), couch);
        Box("Couch paper", new Vector3(0, 0.685f, 0.1f), new Vector3(0.5f, 0.005f, 1.6f), white, couch);
        Box("Pillow", new Vector3(0, 0.72f, 0.78f), new Vector3(0.5f, 0.08f, 0.28f), white, couch);
        foreach (var z in new[] { -0.8f, 0.8f }) Box("Couch leg", new Vector3(0, 0.28f, z), new Vector3(0.5f, 0.56f, 0.06f), metal, couch);
        Box("Curtain rail", new Vector3(0.45f, 2.3f, 0), new Vector3(0.03f, 0.03f, 2.2f), metal, couch);
        Box("Curtain", new Vector3(0.45f, 1.45f, -0.75f), new Vector3(0.02f, 1.6f, 0.7f), C(0.70f, 0.82f, 0.86f), couch);

        // back wall: cabinet, plants, clock; right wall: shelves with books, a picture
        Art("drawer_cabinet", new Vector3(1.2f, 0, Back - 0.28f), 180);
        if (Art("potted_plant_01", new Vector3(-1.95f, 0, Back - 0.4f), 30) == null) {
            Cyl("Plant pot", new Vector3(-1.95f, 0.2f, Back - 0.4f), new Vector3(0.36f, 0.2f, 0.36f), C(0.85f, 0.85f, 0.82f));
            Sphere("Leaves", new Vector3(-1.95f, 0.8f, Back - 0.4f), Vector3.one * 0.55f, C(0.22f, 0.50f, 0.25f));
        }
        Art("potted_plant_02", new Vector3(0.45f, 0, Back - 0.4f), 0);
        var shelves = Art("steel_frame_shelves_01", new Vector3(W / 2 - 0.28f, 0, 2.25f), -90);
        if (shelves != null) {
            OnShelf(shelves, Art("book_encyclopedia_set_01", Vector3.zero, -90), new Vector3(W / 2 - 0.28f, 1.3f, 2.25f));
            OnShelf(shelves, Art("potted_plant_04", Vector3.zero, 0), new Vector3(W / 2 - 0.28f, 1.9f, 2.0f));
            OnShelf(shelves, Art("binder_notebook", Vector3.zero, 80), new Vector3(W / 2 - 0.28f, 0.8f, 2.3f));
        }
        if (Art("wall_clock", new Vector3(1.0f, 2.3f, Back - 0.04f), 180) == null) {
            Cyl("Clock", new Vector3(1.0f, 2.3f, Back - 0.03f), new Vector3(0.32f, 0.01f, 0.32f), white).transform.localRotation = Quaternion.Euler(90, 0, 0);
        }
        if (Art("hanging_picture_frame_02", new Vector3(W / 2 - 0.03f, 1.65f, 1.0f), -90) == null) {
            Box("Picture", new Vector3(W / 2 - 0.045f, 1.6f, 1.0f), new Vector3(0.01f, 0.52f, 0.76f), C(0.45f, 0.62f, 0.78f));
        }
        foreach (var z in new[] { 0.6f, 2.4f })
            if (Art("modern_ceiling_lamp_01", new Vector3(0, H - 1.18f, z), 0) == null)
                Box("Ceiling lamp", new Vector3(0, H - 0.02f, z), new Vector3(1.2f, 0.03f, 0.6f), Glow(new Color(1f, 0.98f, 0.92f), 1.4f));
        Box("Door", new Vector3(-W / 2 + 0.03f, 1.05f, -0.7f), new Vector3(0.04f, 2.1f, 0.95f), cream);
        Box("Door handle", new Vector3(-W / 2 + 0.07f, 1.0f, -0.35f), new Vector3(0.04f, 0.03f, 0.14f), metal);
        Box("Sanitiser", new Vector3(-W / 2 + 0.05f, 1.25f, 0.4f), new Vector3(0.06f, 0.22f, 0.11f), white);
    }

    // Unity's own lights (full VR, Metal rendering): sun through the window with soft shadows, two ceiling lamps,
    // soft three-colour ambient light, and one reflection probe of the room for the shiny surfaces.
    void Lighting() {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.92f, 0.93f, 0.95f);
        RenderSettings.ambientEquatorColor = new Color(0.75f, 0.74f, 0.71f);
        RenderSettings.ambientGroundColor = new Color(0.45f, 0.42f, 0.38f);
        var sky = Resources.Load<Material>("RoomArt/sky");
        if (sky != null) RenderSettings.skybox = sky;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowDistance = 7f;
        QualitySettings.pixelLightCount = 3;
        Lamp(LightType.Directional, Vector3.zero, Quaternion.Euler(38, 195, 0), new Color(1f, 0.95f, 0.86f), 1.1f, 0, true);   // daylight from the window
        Lamp(LightType.Point, new Vector3(0, 1.75f, 0.6f), Quaternion.identity, new Color(1f, 0.95f, 0.88f), 1.2f, 5f, false);
        Lamp(LightType.Point, new Vector3(0, 1.75f, 2.4f), Quaternion.identity, new Color(1f, 0.95f, 0.88f), 1.2f, 5f, false);
        var probe = new GameObject("Reflections").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(transform, false);
        probe.transform.localPosition = new Vector3(0, 1.4f, (Front + Back) / 2);
        probe.size = new Vector3(W, H, Back - Front); probe.boxProjection = true;
        probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; probe.resolution = 128;
        reflections = probe;
    }

    ReflectionProbe reflections;
    void Start() { if (reflections != null) reflections.RenderProbe(); }   // once everything (the patient too) is in place

    /// A Poly Haven model from Resources/RoomArt, standing on `pos` (its origin), turned by `yaw`. Null if not imported.
    static GameObject Art(string id, Vector3 pos, float yaw, Transform parent = null, float scale = 1f) {
        var prefab = Resources.Load<GameObject>("RoomArt/" + id);
        if (prefab == null) return null;
        // a holder keeps the model's own root transform (some FBX roots carry a rotation and scale)
        var holder = new GameObject(id).transform;
        holder.SetParent(parent != null ? parent : current, false);
        holder.SetLocalPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        holder.localScale = Vector3.one * scale;
        Instantiate(prefab, holder, false);
        return holder.gameObject;
    }
    static Transform current;

    /// Stands `item` on the first shelf below `from` (a ray down onto the shelf unit's meshes).
    static void OnShelf(GameObject shelves, GameObject item, Vector3 from) {
        if (item == null) return;
        var cols = new System.Collections.Generic.List<MeshCollider>();
        foreach (var mf in shelves.GetComponentsInChildren<MeshFilter>()) cols.Add(mf.gameObject.AddComponent<MeshCollider>());
        Physics.SyncTransforms();
        var y = Physics.Raycast(from, Vector3.down, out var hit, 2.5f) ? hit.point.y : 0f;
        foreach (var c in cols) if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        item.transform.position = new Vector3(from.x, y, from.z);
    }

    /// A vase of flowers: stems, leaves and coloured blossoms (in Poly Haven's ceramic vase when it is there).
    void Flowers(Vector3 at, float scale) {
        var vase = new GameObject("Flowers").transform; vase.SetParent(transform, false);
        vase.position = at; vase.localScale = Vector3.one * scale;
        const float neck = 0.2f;   // both vases are 0.2 m tall
        if (Art("ceramic_vase_02", Vector3.zero, 0, vase, 0.65f) == null)
            Cyl("Vase", new Vector3(0, 0.1f, 0), new Vector3(0.11f, 0.1f, 0.11f), C(0.30f, 0.52f, 0.72f), vase);
        var colours = new[] { C(0.95f, 0.45f, 0.55f), C(0.98f, 0.85f, 0.30f), C(0.85f, 0.30f, 0.35f), C(0.98f, 0.98f, 0.95f), C(0.75f, 0.50f, 0.85f), C(0.98f, 0.60f, 0.30f), C(0.95f, 0.45f, 0.55f) };
        var green = C(0.25f, 0.55f, 0.25f);
        for (int i = 0; i < colours.Length; i++) {
            float a = i * 2.4f, r = 0.03f + 0.03f * (i % 3), h = 0.36f + 0.06f * ((i * 7) % 3);
            var topPt = new Vector3(Mathf.Cos(a) * r * 2.2f, h, Mathf.Sin(a) * r * 2.2f);
            var bottom = new Vector3(0, neck - 0.05f, 0);
            var stem = Cyl("Stem", (topPt + bottom) / 2, new Vector3(0.008f, (topPt - bottom).magnitude / 2, 0.008f), green, vase);
            stem.transform.localRotation = Quaternion.FromToRotation(Vector3.up, topPt - bottom);
            Sphere("Blossom", topPt, new Vector3(0.07f, 0.05f, 0.07f), colours[i], vase);
            Sphere("Centre", topPt + new Vector3(0, 0.022f, 0), Vector3.one * 0.022f, C(0.95f, 0.80f, 0.20f), vase);
            Sphere("Leaf", Vector3.Lerp(bottom, topPt, 0.55f), new Vector3(0.05f, 0.012f, 0.03f), green, vase);
        }
    }

    void Lamp(LightType type, Vector3 pos, Quaternion rot, Color color, float intensity, float range, bool shadows) {
        var l = new GameObject(type + " light").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.SetLocalPositionAndRotation(pos, rot);
        l.type = type; l.color = color; l.intensity = intensity;
        if (range > 0) l.range = range;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.shadowStrength = 0.75f;
    }

    static Vector3 Inward(Vector3 wallPos) => wallPos.x > 2 ? Vector3.right : wallPos.x < -2 ? Vector3.left : wallPos.z > 3 ? Vector3.forward : Vector3.back;
    static Vector3 Thick(Vector3 size) => size.x < 0.1f ? Vector3.right : Vector3.forward;

    static Material baseMat;
    static Material C(float r, float g, float b, float smooth = 0.25f) {
        baseMat ??= Resources.Load<Material>("PatientMaterial");
        var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
        m.color = new Color(r, g, b);
        m.SetFloat("_Glossiness", smooth);
        return m;
    }
    // Copies Resources/GlowMaterial (Standard with emission on), so the emissive shader variant is in the build.
    static Material glowMat;
    static Material Glow(Color c, float strength) {
        glowMat ??= Resources.Load<Material>("GlowMaterial");
        var m = glowMat != null ? new Material(glowMat) : C(c.r, c.g, c.b);
        m.color = c;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * strength);
        return m;
    }

    GameObject Box(string n, Vector3 p, Vector3 s, Material m, Transform parent = null) => Prim(PrimitiveType.Cube, n, p, s, m, parent);
    GameObject Cyl(string n, Vector3 p, Vector3 s, Material m, Transform parent = null) => Prim(PrimitiveType.Cylinder, n, p, s, m, parent);
    GameObject Sphere(string n, Vector3 p, Vector3 s, Material m, Transform parent = null) => Prim(PrimitiveType.Sphere, n, p, s, m, parent);
    GameObject Prim(PrimitiveType t, string n, Vector3 p, Vector3 s, Material m, Transform parent) {
        var go = GameObject.CreatePrimitive(t);
        go.name = n;
        var col = go.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = p; go.transform.localScale = s;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

}
