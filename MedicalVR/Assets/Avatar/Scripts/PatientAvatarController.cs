using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MedicalVR.PatientAvatar
{
    /// Runtime bridge for an Ara FBX/GLB prefab. Ara supplies the mesh, rig,
    /// clips, and blend shapes; this component maps neutral API commands to them.
    public sealed class PatientAvatarController : MonoBehaviour
    {
        [Header("Ara character")]
        [SerializeField] private Animator animator;
        [SerializeField] private AudioSource speechAudio;
        [SerializeField] private SkinnedMeshRenderer[] faceMeshes;
        [SerializeField] private Transform gazeTarget;
        [SerializeField] private Transform head;

        [Header("Ara clip names")]
        [SerializeField] private string idleClip = "Idle";
        [SerializeField] private string talkingClip = "Talk with gestures";
        [SerializeField] private string listeningClip = "Sit idle";
        [SerializeField] private string discomfortClip = "Sit idle";

        [Header("Tuning")]
        [SerializeField, Range(0f, 180f)] private float maxGazeYaw = 55f;
        [SerializeField] private float transitionSeconds = 0.25f;

        private Coroutine expressionRoutine;
        private Coroutine discomfortRoutine;
        private readonly Dictionary<string, float> expressionWeights = new Dictionary<string, float>();

        public void Apply(AvatarCommand command, AudioClip audio = null)
        {
            if (command == null) return;
            SetEmotion(command.behavior.emotion, command.behavior.intensity);
            SetGaze(command.behavior.gaze);
            TriggerGesture(command.behavior.gesture);
            SetBodyState(command.body_state);
            if (audio != null) Speak(audio);
        }

        public void SetEmotion(PatientEmotion emotion, float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            var weights = EmotionWeights(emotion, intensity);
            if (expressionRoutine != null) StopCoroutine(expressionRoutine);
            expressionRoutine = StartCoroutine(BlendExpression(weights));
        }

        public void SetBodyState(string state)
        {
            if (animator == null) return;
            var clip = state switch
            {
                "seated_talking" => talkingClip,
                "seated_listening" => listeningClip,
                "seated_discomfort" => discomfortClip,
                _ => idleClip
            };
            animator.CrossFade(clip, transitionSeconds);
        }

        public void SetGaze(PatientGaze gaze)
        {
            if (head == null || gazeTarget == null || gaze != PatientGaze.student) return;
            var direction = gazeTarget.position - head.position;
            var target = Quaternion.LookRotation(direction, Vector3.up);
            var limited = Quaternion.RotateTowards(head.rotation, target, maxGazeYaw);
            head.rotation = Quaternion.Slerp(head.rotation, limited, 0.2f);
        }

        public void TriggerGesture(PatientGesture gesture)
        {
            if (gesture == PatientGesture.hold_abdomen)
            {
                if (discomfortRoutine != null) StopCoroutine(discomfortRoutine);
                discomfortRoutine = StartCoroutine(DiscomfortEpisode());
            }
            else if (animator != null && gesture != PatientGesture.none)
            {
                animator.SetTrigger(gesture.ToString());
            }
        }

        public void Speak(AudioClip clip)
        {
            if (speechAudio == null || clip == null) return;
            speechAudio.Stop();
            speechAudio.clip = clip;
            speechAudio.Play();
            SetBodyState("seated_talking");
        }

        public void StopSpeaking()
        {
            if (speechAudio != null) speechAudio.Stop();
            SetBodyState("seated_idle");
        }

        public void ResetPatient()
        {
            StopSpeaking();
            SetEmotion(PatientEmotion.neutral, 0f);
            SetBodyState("seated_idle");
        }

        private IEnumerator BlendExpression(Dictionary<string, float> target)
        {
            var start = new Dictionary<string, float>(expressionWeights);
            var elapsed = 0f;
            while (elapsed < transitionSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, elapsed / transitionSeconds);
                foreach (var item in target)
                    SetShape(item.Key, Mathf.Lerp(start.TryGetValue(item.Key, out var value) ? value : 0f, item.Value, t));
                yield return null;
            }
            foreach (var item in target) SetShape(item.Key, item.Value);
        }

        private Dictionary<string, float> EmotionWeights(PatientEmotion emotion, float amount)
        {
            var weights = new Dictionary<string, float>();
            if (emotion == PatientEmotion.anxious) { weights["browInnerUp"] = amount; weights["eyeWideLeft"] = amount * .45f; weights["eyeWideRight"] = amount * .45f; }
            if (emotion == PatientEmotion.in_pain || emotion == PatientEmotion.annoyed) { weights["browDownLeft"] = amount; weights["browDownRight"] = amount; weights["mouthFrown"] = amount * .7f; }
            if (emotion == PatientEmotion.relieved) { weights["mouthSmile"] = amount * .65f; }
            if (emotion == PatientEmotion.sad) { weights["browInnerUp"] = amount * .7f; weights["mouthFrown"] = amount; }
            if (emotion == PatientEmotion.embarrassed) { weights["browInnerUp"] = amount * .45f; weights["mouthPress"] = amount * .55f; }
            return weights;
        }

        private void SetShape(string name, float value)
        {
            expressionWeights[name] = value;
            foreach (var mesh in faceMeshes)
            {
                if (mesh == null) continue;
                var index = mesh.sharedMesh.GetBlendShapeIndex(name);
                if (index >= 0) mesh.SetBlendShapeWeight(index, value * 100f);
            }
        }

        private IEnumerator DiscomfortEpisode()
        {
            SetBodyState("seated_discomfort");
            SetEmotion(PatientEmotion.in_pain, .65f);
            yield return new WaitForSeconds(3.5f);
            SetEmotion(PatientEmotion.neutral, .15f);
            SetBodyState("seated_idle");
        }
    }
}
