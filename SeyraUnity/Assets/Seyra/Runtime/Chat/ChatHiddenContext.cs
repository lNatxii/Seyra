using System;

namespace Seyra.Chat
{
    public static class ChatHiddenContext
    {
        public const string Prefix = "[selected-object] ";
        public static string Compose(string context, string text) => string.IsNullOrEmpty(context) ? text : Prefix + context + "\n" + text;
        public static string Strip(string text)
        {
            if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal)) return text;
            int newline = text.IndexOf('\n');
            return newline < 0 ? text : text.Substring(newline + 1);
        }
    }
}
