using UnityEngine;

/// What ui needs from the patient's body. avatar's prefab implements this later (avatar/CONTRACT.md).
public interface IPatient {
    /// One of ai/CONTRACT.md's emotions: neutral, anxious, in_pain, relieved, annoyed, sad, embarrassed.
    void SetEmotion(string emotion);
    /// Speak the reply: plays the clip and moves the mouth with it.
    void PlayReply(AudioClip clip);
}

// shortcut: patient from primitives (seated body, head, jaw that opens with the audio),
// until avatar publishes its prefab or ui drops in the Rocketbox placeholder lead suggested.
public class PlaceholderPatient : MonoBehaviour, IPatient {
    AudioSource voice;
    Transform jaw;
    Material headMat;
    readonly float[] buf = new float[256];
    static Material baseMat;

    public static PlaceholderPatient Create(Vector3 position, Vector3 lookAt) {
        var root = new GameObject("Patient (placeholder)");
        root.transform.position = position;
        var flat = lookAt - position; flat.y = 0;
        root.transform.rotation = Quaternion.LookRotation(flat);

        MakeChair(root.transform);
        Part(root, PrimitiveType.Capsule, "Body", new Vector3(0, 0.82f, -0.08f), new Vector3(0.38f, 0.32f, 0.26f), new Color(0.3f, 0.55f, 0.6f));
        Part(root, PrimitiveType.Cube, "Legs", new Vector3(0, 0.52f, 0.12f), new Vector3(0.32f, 0.12f, 0.4f), new Color(0.2f, 0.2f, 0.3f));
        var head = Part(root, PrimitiveType.Sphere, "Head", new Vector3(0, 1.28f, -0.06f), new Vector3(0.2f, 0.24f, 0.22f), new Color(0.9f, 0.75f, 0.65f));
        Part(head, PrimitiveType.Sphere, "Nose", new Vector3(0, 0, 0.5f), Vector3.one * 0.15f, new Color(0.85f, 0.65f, 0.55f));
        var jaw = Part(head, PrimitiveType.Cube, "Mouth", new Vector3(0, -0.28f, 0.45f), new Vector3(0.35f, 0.04f, 0.1f), new Color(0.4f, 0.1f, 0.1f));

        var p = root.AddComponent<PlaceholderPatient>();
        p.voice = root.AddComponent<AudioSource>();
        p.voice.spatialBlend = 1; p.voice.minDistance = 1;
        p.jaw = jaw.transform;
        p.headMat = head.GetComponent<Renderer>().material;
        return p;
    }

    /// The chair the patient sits on (same room and chair for every case). Local to the patient, facing +z.
    public static void MakeChair(Transform patient, float seat = ChairSeatHeight, float seatZ = -0.05f) {
        var wood = new Color(0.35f, 0.25f, 0.2f);
        Part(patient.gameObject, PrimitiveType.Cube, "Chair", new Vector3(0, seat - 0.025f, seatZ), new Vector3(0.5f, 0.05f, 0.5f), wood);
        Part(patient.gameObject, PrimitiveType.Cube, "Backrest", new Vector3(0, seat + 0.3f, seatZ - 0.23f), new Vector3(0.5f, 0.6f, 0.05f), wood);
        for (int i = 0; i < 4; i++)
            Part(patient.gameObject, PrimitiveType.Cube, "Leg", new Vector3(i % 2 == 0 ? -0.22f : 0.22f, (seat - 0.05f) / 2, seatZ + (i < 2 ? -0.22f : 0.22f)),
                 new Vector3(0.04f, seat - 0.05f, 0.04f), wood);
    }
    public const float ChairSeatHeight = 0.45f;

    static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color) {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        baseMat ??= Resources.Load<Material>("PatientMaterial");
        if (baseMat != null) r.material = new Material(baseMat);
        r.material.color = color;
        return go;
    }

    public void SetEmotion(string emotion) {
        // Placeholder cue: tint the face. avatar replaces this with real expressions.
        headMat.color = emotion switch {
            "anxious" => new Color(0.95f, 0.85f, 0.55f),
            "in_pain" => new Color(0.95f, 0.6f, 0.55f),
            "sad" => new Color(0.7f, 0.75f, 0.9f),
            "annoyed" => new Color(0.95f, 0.65f, 0.4f),
            "embarrassed" => new Color(0.95f, 0.6f, 0.7f),
            _ => new Color(0.9f, 0.75f, 0.65f),
        };
    }

    public void PlayReply(AudioClip clip) {
        if (clip == null) return;
        voice.clip = clip;
        voice.Play();
    }

    void Update() {
        // Jaw opens with the loudness of the reply (fallback lip sync, hard rule 5).
        float level = 0;
        if (voice.isPlaying) {
            voice.GetOutputData(buf, 0);
            foreach (var s in buf) level = Mathf.Max(level, Mathf.Abs(s));
        }
        var sc = jaw.localScale;
        sc.y = Mathf.Lerp(sc.y, 0.04f + Mathf.Clamp01(level * 3) * 0.25f, Time.deltaTime * 20);
        jaw.localScale = sc;
    }
}
