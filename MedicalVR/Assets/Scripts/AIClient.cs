using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// Client for ai/CONTRACT.md v1, Way A (one request per turn).
// The app holds no medical facts: everything about the patient comes from ai.
// Field names match the JSON keys (snake_case) so JsonUtility can read them.

[Serializable] public class CaseCard {
    public string id, title, complaint, door_note, name, sex, mood, difficulty, avatar;
    public int age;
}
[Serializable] class CaseList { public CaseCard[] items; }
[Serializable] public class SessionStart { public string session_id; public CaseCard @case; }
[Serializable] public class TurnReply {
    public string student_text, patient_text, emotion, gesture, audio_base64, audio_format;
    public bool fallback;
}
[Serializable] public class FeedbackItem {
    public string id, text, phase, evidence, reference_line;   // reference_line: what the model student said (ai, 117e4c6)
    public bool achieved, in_reference;
    public float at_s;
}
[Serializable] public class MissedFact { public string text; public bool red_flag; }
[Serializable] public class Feedback {
    public float overall_percent, rubric_percent;
    public string graded_against;   // "reference" | "rubric"
    public FeedbackItem[] items;
    public string[] strengths, suggestions;
    public MissedFact[] missed_facts;
    public string graded_by;
}

public interface IAIClient {
    Task<CaseCard[]> Cases();
    Task<SessionStart> StartSession(string caseId);
    /// Send either text (typed fallback) or wav (recorded question).
    Task<TurnReply> Turn(string sessionId, string text, byte[] wav);
    Task<Feedback> End(string sessionId);
}

public class HttpAIClient : IAIClient {
    readonly string baseUrl;
    public HttpAIClient(string baseUrl) { this.baseUrl = baseUrl.TrimEnd('/'); }

    /// True if ai answers GET /health at `url` within `seconds`.
    public static async Task<bool> Answers(string url, int seconds = 3) {
        using var req = UnityWebRequest.Get(url.TrimEnd('/') + "/health");
        req.timeout = seconds;
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        return req.result == UnityWebRequest.Result.Success;
    }

    async Task<string> Send(string path, string jsonBody) {
        using var req = jsonBody == null
            ? UnityWebRequest.Get(baseUrl + path)
            : new UnityWebRequest(baseUrl + path, "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer() };
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = path.EndsWith("/end") ? 60 : 30;   // grading with the LLM takes ~25 s; everything else is quick
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception($"ai {req.responseCode} {path}: {req.error} {req.downloadHandler?.text}");
        return req.downloadHandler.text;
    }

    public async Task<CaseCard[]> Cases() =>
        // JsonUtility cannot read a top-level array, so wrap it.
        JsonUtility.FromJson<CaseList>("{\"items\":" + await Send("/cases", null) + "}").items;

    public async Task<SessionStart> StartSession(string caseId) =>
        JsonUtility.FromJson<SessionStart>(await Send("/api/sessions", $"{{\"case_id\":{Q(caseId)}}}"));

    public async Task<TurnReply> Turn(string sessionId, string text, byte[] wav) {
        var body = wav != null
            ? $"{{\"audio_base64\":\"{Convert.ToBase64String(wav)}\",\"audio_format\":\"wav\"}}"
            : $"{{\"text\":{Q(text ?? "")}}}";
        return JsonUtility.FromJson<TurnReply>(await Send($"/api/sessions/{sessionId}/turn", body));
    }

    public async Task<Feedback> End(string sessionId) =>
        JsonUtility.FromJson<Feedback>(await Send($"/api/sessions/{sessionId}/end", "{}"));

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}

// shortcut: canned mock without medical content, until the app runs against ai's live server.
public class MockAIClient : IAIClient {
    static readonly CaseCard Card = new CaseCard {
        id = "mock_case", title = "Mock case (no server)", complaint = "Placeholder complaint",
        name = "Mock Patient", age = 40, sex = "female", mood = "neutral" };

    public Task<CaseCard[]> Cases() => Task.FromResult(new[] { Card });
    public Task<SessionStart> StartSession(string caseId) =>
        Task.FromResult(new SessionStart { session_id = "mock-session", @case = Card });
    public async Task<TurnReply> Turn(string sessionId, string text, byte[] wav) {
        await Task.Delay(600);
        var heard = text ?? $"(audio, {wav?.Length ?? 0} bytes)";
        return new TurnReply { student_text = heard, patient_text = "Mock reply to: " + heard,
                               emotion = "neutral", gesture = "none" };
    }
    public Task<Feedback> End(string sessionId) => Task.FromResult(new Feedback {
        overall_percent = 50, graded_by = "mock",
        items = new[] {
            new FeedbackItem { id = "intro_self", text = "Introduces self (mock)", achieved = true,
                               evidence = "Hello, I'm a student.", at_s = 0 },
            new FeedbackItem { id = "mock_missed", text = "Mock missed item", achieved = false } },
        strengths = new[] { "Mock strength" }, suggestions = new[] { "Mock suggestion" } });
}

public static class ReplyAudio {
    /// The reply's audio as a clip, or null (then show subtitles only).
    /// ai sends "wav" (live speech) or "mp3" (offline voice cache, ai 9a4683f).
    public static async Task<AudioClip> Load(TurnReply r) {
        if (string.IsNullOrEmpty(r.audio_base64)) return null;
        try {
            var bytes = Convert.FromBase64String(r.audio_base64);
            if (r.audio_format != "mp3") return Wav.ToClip(bytes);
            // Unity decodes MP3 only through a URL, so go via a temp file.
            var path = System.IO.Path.Combine(Application.temporaryCachePath, "reply.mp3");
            System.IO.File.WriteAllBytes(path, bytes);
            using var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG);
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result == UnityWebRequest.Result.Success) return DownloadHandlerAudioClip.GetContent(req);
            Debug.Log("[MedicalVR] mp3 not decoded: " + req.error);
            return null;
        } catch (Exception e) {
            Debug.Log("[MedicalVR] reply audio not playable, subtitles only: " + e.Message);
            return null;
        }
    }
}

/// WAV (PCM16 mono) to and from Unity audio: the reply from ai, and the recorded question.
public static class Wav {
    public static AudioClip ToClip(byte[] wav, string name = "reply") {
        int channels = BitConverter.ToInt16(wav, 22), rate = BitConverter.ToInt32(wav, 24);
        int pos = 12;
        while (pos + 8 <= wav.Length && Encoding.ASCII.GetString(wav, pos, 4) != "data")
            pos += 8 + BitConverter.ToInt32(wav, pos + 4);
        int len = BitConverter.ToInt32(wav, pos + 4); pos += 8;
        len = Math.Min(len, wav.Length - pos);
        var samples = new float[len / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(wav, pos + i * 2) / 32768f;
        var clip = AudioClip.Create(name, samples.Length / channels, channels, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public static byte[] FromSamples(float[] samples, int rate) {
        var data = new List<byte>(44 + samples.Length * 2);
        void S(string s) => data.AddRange(Encoding.ASCII.GetBytes(s));
        void I(int v) => data.AddRange(BitConverter.GetBytes(v));
        void H(short v) => data.AddRange(BitConverter.GetBytes(v));
        S("RIFF"); I(36 + samples.Length * 2); S("WAVE"); S("fmt "); I(16); H(1); H(1);
        I(rate); I(rate * 2); H(2); H(16); S("data"); I(samples.Length * 2);
        foreach (var f in samples) H((short)(Mathf.Clamp(f, -1f, 1f) * 32767));
        return data.ToArray();
    }
}
