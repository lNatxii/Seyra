using System;
using Seyra.Voice;

public static class SpokenReplyChecks
{
    public static string RunStates()
    {
        var previous = UnityEngine.PlayerPrefs.GetInt("Seyra.Voice.Muted", 0);
        var host = new UnityEngine.GameObject("__SpokenReplyChecks");
        var voice = host.AddComponent<SpokenReplies>();
        voice.SimulateOutput = true;
        try
        {
            voice.SetMuted(true); voice.SpeakReply("First reply.");
            Check(voice.HasReply && voice.State == SpokenReplyState.Idle && voice.LastSpokenText == "", "Mute must suppress automatic speech.");
            voice.Replay();
            Check(voice.State == SpokenReplyState.Speaking && voice.LastSpokenText == "First reply.", "Explicit replay must work while muted.");
            voice.SetMuted(false); voice.SpeakReply("Second reply.");
            Check(voice.LastSpokenText == "Second reply.", "A new reply must replace the old output.");
            voice.SimulateFailure();
            Check(voice.State == SpokenReplyState.Idle && voice.Status == "Could not read the reply aloud. The text is still in the chat.", "Failure must leave a safe status and no active output.");
            return "PASS: mute, explicit replay, replacement and simulated failure.";
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.PlayerPrefs.SetInt("Seyra.Voice.Muted", previous); UnityEngine.PlayerPrefs.Save(); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string RunFilter()
    {
        Check(SpeechText.Prepare("{\"answer\":42}") == "" && SpeechText.Prepare("[1,2]") == "", "Whole JSON must not be spoken.");
        Check(SpeechText.Prepare("```cs\nSecret();\n```") == "", "Code must not be spoken.");
        Check(SpeechText.Prepare("# Hello\n- **Read** [this](https://example.com)\n> `Now` https://example.com") == "Hello. Read this. Now.", "Markdown, links and line pauses must be prepared.");
        var longText = SpeechText.Prepare(string.Join(" ", System.Linq.Enumerable.Repeat("This is a sentence.", 60)));
        Check(longText.Length <= SpeechText.MaxSpokenCharacters && longText.EndsWith("The rest is in the chat."), "Long text must end at a sentence with a bounded notice.");
        Check(SpeechText.Prepare(new string('a', 700)).Length <= SpeechText.MaxSpokenCharacters, "Unpunctuated text must remain bounded.");
        return "PASS: speech text filter.";
    }
}
