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
            voice.SetMuted(false); voice.SpeakReply("First reply.");
            Check(voice.State == SpokenReplyState.Speaking, "Unmuted replies must play.");
            voice.ToggleMuted();
            Check(voice.Muted && voice.State == SpokenReplyState.Idle && voice.LastSpokenText == "", "Muting must stop output.");
            voice.SpeakReply("Muted reply.");
            Check(voice.State == SpokenReplyState.Idle && voice.LastSpokenText == "", "Muted replies must not play.");
            voice.ToggleMuted();
            Check(!voice.Muted && voice.State == SpokenReplyState.Idle && voice.LastSpokenText == "", "Unmuting must not replay an old reply.");
            voice.SpeakReply("Second reply.");
            Check(voice.LastSpokenText == "Second reply.", "Only a new reply may start output.");
            voice.SimulateFailure();
            Check(voice.State == SpokenReplyState.Idle && voice.Status == "Could not read the reply aloud. The text is still in the chat.", "Failure must leave a safe status and no active output.");
            return "PASS: toggle, muting stops output, unmuting does not replay and simulated failure.";
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
