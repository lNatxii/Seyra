using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Seyra.Voice
{
    public static class SpeechText
    {
        public const int MaxSpokenCharacters = 600;
        const string Remainder = "The rest is in the chat.";

        public static string Prepare(string reply)
        {
            var text = (reply ?? "").Trim();
            if (text.StartsWith("{") || text.StartsWith("["))
            {
                try { JToken.Parse(text); return ""; }
                catch (JsonException) { }
            }
            text = Regex.Replace(text, @"```[\s\S]*?(?:```|$)", "");
            text = Regex.Replace(text, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"(?:https?://|www\.)\S+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?m)^\s*(?:#{1,6}\s+|>\s*|[-+*]\s+|\d+[.)]\s+)", "");
            text = Regex.Replace(text, @"[*_`|]", "");
            var lines = text.Replace("\r", "").Split('\n').Select(line => Regex.Replace(line, @"\s+", " ").Trim()).Where(line => line.Length > 0);
            text = string.Join(" ", lines.Select(line => ".,;:!?".Contains(line[line.Length - 1]) ? line : line + ".")).Trim();
            if (text.Length <= MaxSpokenCharacters) return text;
            // Reserve room for the notice so the total stays within the spoken limit.
            var prefix = text.Substring(0, MaxSpokenCharacters - Remainder.Length - 1);
            var end = prefix.LastIndexOfAny(new[] { '.', '?', '!' });
            if (end >= 0) prefix = prefix.Substring(0, end + 1);
            else { end = prefix.LastIndexOf(' '); if (end >= 0) prefix = prefix.Substring(0, end); }
            return prefix.TrimEnd() + " " + Remainder;
        }
    }
}
