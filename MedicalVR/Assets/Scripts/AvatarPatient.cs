using System;
using System.Collections.Generic;
using System.Linq;
using MedicalVR.PatientAvatar;
using UnityEngine;

/// ui's side of avatar's Hannah (`avatar/CONTRACT.md` v2): ui hands her the emotion cue and the reply audio.
/// Body: avatar's clips (Hannah_anims.zip) "Sit idle" always, "Talk with gestures" blended in on the arms while she
/// speaks. Face and voice: avatar's PatientAvatarController (emotion blend shapes, AudioSource). On top: lip sync
/// from the audio, blinking, and the head turned to the student.
public class AvatarPatient : MonoBehaviour, IPatient {
    [SerializeField] PatientAvatarController controller;
    [SerializeField] Animator animator;
    [SerializeField] AudioSource voice;
    [SerializeField] SkinnedMeshRenderer[] faces;
    [SerializeField] AnimationClip sitClip, talkClip;

    /// Where the student's eyes are (the origin of the room, standing). ui can change it.
    public Vector3 StudentEyes = new Vector3(0, 1.5f, 0);
    /// Seat top height and its centre (local z) for the chair, measured from her seated pose.
    public float SeatHeight { get; private set; } = 0.45f;
    public float SeatZ { get; private set; } = -0.05f;
    /// Where her hips are, local z (seated clips move the body away from the model origin).
    public float HipsZ { get; private set; }

    // bone names on Hannah (MakeHuman) and on Rocketbox ("Bip01 L UpperArm", ...), compared in lower case
    static readonly string[] UpperBody = { "clavicle", "shoulder", "upperarm", "lowerarm", "forearm", "wrist", "hand", "finger", "metacarpal", "neck" };
    readonly float[] samples = new float[256];
    Transform head;
    Vector3 headFace;   // the head's local axis that points out of her face (from the bind pose, looking ahead)
    Transform[] upper;
    Quaternion[] sitRot;
    bool speaking;
    float gestureUntil;
    float talk, level, jaw, nextBlink = 2f;

    public void SetEmotion(string emotion) {
        if (controller == null) return;
        var e = Enum.TryParse(emotion, out PatientEmotion parsed) ? parsed : PatientEmotion.neutral;
        controller.SetEmotion(e, 0.8f);
    }

    public void PlayReply(AudioClip clip) {
        if (clip == null || controller == null) return;
        controller.Speak(clip);
        speaking = true;
    }

    /// Talking gestures for `seconds` without audio (the doctor presenting the feedback).
    public void Gesture(float seconds) => gestureUntil = Time.time + seconds;
    void Awake() { Init(); }

    /// Maps the bones and measures the seat (also outside play mode, where Awake does not run).
    public void Init() {
        if (upper != null) return;
        if (animator == null) animator = GetComponent<Animator>();
        head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var knee = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) : null;
        var bones = GetComponentsInChildren<Transform>(true);
        Transform Find(params string[] names) => bones.FirstOrDefault(t => names.Contains(t.name.ToLower()));
        if (head == null) head = Find("head", "bip01 head");
        if (hips == null) hips = Find("pelvis", "bip01 pelvis");
        if (knee == null) knee = Find("lowerleg01.l", "bip01 l calf");
        if (head != null) headFace = Quaternion.Inverse(head.rotation) * transform.forward;
        // The Animator only maps the bones. A humanoid Animator would treat the hips curve as root motion when the
        // clips are sampled (she sank 0.55 m into the floor), so it goes once the bones are known.
        if (animator != null) { DestroyImmediate(animator); animator = null; }   // now, not at the end of the frame
        upper = bones.Where(t => UpperBody.Any(k => t.name.ToLower().Contains(k)) && !t.name.ToLower().Contains("nub")).ToArray();
        sitRot = new Quaternion[upper.Length];
        if (sitClip != null && hips != null && knee != null) {
            sitClip.SampleAnimation(gameObject, 0f);
            var h = transform.InverseTransformPoint(hips.position);
            var k = transform.InverseTransformPoint(knee.position);
            SeatHeight = Mathf.Max(0.3f, Mathf.Min(h.y, k.y) - 0.09f);   // seat top under the thighs (hip and knee joints sit ~9 cm above it)
            SeatZ = h.z + 0.12f;
            HipsZ = h.z;                                 // hips near the back of a 0.5 m deep seat
            Debug.Log($"[MedicalVR] {name} pose: hips y {h.y:0.00} z {h.z:0.00}, knee y {k.y:0.00} z {k.z:0.00}");
        }
    }

    void Update() {
        if (speaking && !voice.isPlaying) { speaking = false; controller.StopSpeaking(); }
        float level01 = 0f;
        if (voice != null && voice.isPlaying) {
            voice.GetOutputData(samples, 0);
            foreach (var s in samples) level01 = Mathf.Max(level01, Mathf.Abs(s));
        }
        level = Mathf.Lerp(level, level01, Time.deltaTime * 12f);
        talk = Mathf.MoveTowards(talk, speaking || Time.time < gestureUntil ? 1f : 0f, Time.deltaTime * 2f);
    }

    void LateUpdate() {
        if (Camera.main != null) StudentEyes = Camera.main.transform.position;   // she looks at the student's head
        Pose(Time.time, talk);

        // shortcut: jaw from loudness (no visemes yet); the model has all 14 viseme shapes for a later upgrade.
        jaw = Mathf.Lerp(jaw, Mathf.Clamp01(level * 2.5f), Time.deltaTime * 20f);
        SetShape("jawOpen", jaw * 0.7f);
        nextBlink -= Time.deltaTime;
        float blink = nextBlink < 0.12f ? 1f : 0f;
        if (nextBlink < 0f) nextBlink = UnityEngine.Random.Range(2.5f, 5.5f);
        SetShape("eyeBlinkLeft", blink); SetShape("eyeBlinkRight", blink);
        Gaze();
    }
    void Gaze() {
        if (head != null) {
            // the clip lets her look down at her lap; turn the head (up to 45 degrees) to the student's eyes
            var face = head.rotation * headFace;
            var want = StudentEyes - head.position;
            if (want.sqrMagnitude > 0.01f) {
                var turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(face, want.normalized), 45f);
                head.rotation = turn * head.rotation;
            }
        }
    }
    /// Seated idle, with the talking clip on the arms and neck by `talkWeight` (0..1).
    void Pose(float t, float talkWeight) {
        if (sitClip == null) return;
        sitClip.SampleAnimation(gameObject, t % sitClip.length);
        if (talkClip == null || talkWeight <= 0.001f) return;
        for (int i = 0; i < upper.Length; i++) sitRot[i] = upper[i].localRotation;
        talkClip.SampleAnimation(gameObject, t % talkClip.length);
        for (int i = 0; i < upper.Length; i++) upper[i].localRotation = Quaternion.Slerp(sitRot[i], upper[i].localRotation, talkWeight);
    }

    /// Editor render check (AvatarImport.RenderCheck): pose without play mode.
    public void PoseForEditor(float t, float talkWeight) { Init(); Pose(t, talkWeight); Gaze(); }

    // Shape names differ per model: Hannah "jawOpen", Rocketbox "blendShape1.AK_25_JawOpen"; matched by ending.
    readonly Dictionary<(SkinnedMeshRenderer, string), int> shapeIndex = new();
    void SetShape(string shape, float weight01) {
        foreach (var mesh in faces) {
            if (mesh == null || mesh.sharedMesh == null) continue;
            if (!shapeIndex.TryGetValue((mesh, shape), out int i)) {
                i = -1;
                var key = shape.ToLower();
                for (int k = 0; k < mesh.sharedMesh.blendShapeCount; k++)
                    if (mesh.sharedMesh.GetBlendShapeName(k).ToLower().EndsWith(key)) { i = k; break; }
                shapeIndex[(mesh, shape)] = i;
            }
            if (i >= 0) mesh.SetBlendShapeWeight(i, weight01 * 100f);
        }
    }
}
