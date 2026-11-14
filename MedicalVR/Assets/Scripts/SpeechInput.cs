using System.Runtime.InteropServices;
using UnityEngine;

/// The headset's own speech-to-text (Plugins/visionOS/SpeechBridge.mm). The student's words go to ai as text,
/// so voice works even when ai has no speech-to-text key. In the editor it reports "not available".
public static class SpeechInput {
#if UNITY_VISIONOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void MVR_SpeechAuthorize();
    [DllImport("__Internal")] static extern bool MVR_SpeechAvailable();
    [DllImport("__Internal")] static extern bool MVR_SpeechStart(string gameObject);
    [DllImport("__Internal")] static extern void MVR_SpeechStop();
    public static void Authorize() => MVR_SpeechAuthorize();
    public static bool Available => MVR_SpeechAvailable();
    /// Starts listening; the text arrives later as SendMessage("OnSpeechResult", text) on `receiver`.
    public static bool Start(GameObject receiver) => MVR_SpeechStart(receiver.name);
    public static void Stop() => MVR_SpeechStop();
#else
    public static void Authorize() { }
    public static bool Available => false;
    public static bool Start(GameObject receiver) => false;
    public static void Stop() { }
#endif
}
