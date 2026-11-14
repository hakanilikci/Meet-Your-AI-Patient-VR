using System;
using UnityEngine;

namespace MedicalVR.PatientAvatar
{
    public enum PatientEmotion { neutral, anxious, in_pain, relieved, annoyed, sad, embarrassed }
    public enum PatientGesture { none, nod, shake_head, open_hands, shrug, adjust_posture, hold_abdomen, small_head_nod, look_down }
    public enum PatientGaze { student, forward, down, away }

    [Serializable]
    public sealed class AvatarSpeech
    {
        public string text;
        public string language = "de";
        public string voice_id;
        public bool lip_sync = true;
    }

    [Serializable]
    public sealed class AvatarBehavior
    {
        public PatientEmotion emotion = PatientEmotion.neutral;
        [Range(0f, 1f)] public float intensity;
        public PatientGesture gesture = PatientGesture.none;
        public PatientGaze gaze = PatientGaze.student;
    }

    [Serializable]
    public sealed class AvatarCommand
    {
        public string patient_id = "patient_001";
        public string body_state = "seated_idle";
        public AvatarBehavior behavior = new AvatarBehavior();
        public AvatarSpeech speech = new AvatarSpeech();
    }
}
