using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Networking;
using Seyra.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives the actual chat controls; never dispatches scene tools directly.
public static class ConversationCheck
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(OpenCodeTestChat chat, string name) => (T)typeof(OpenCodeTestChat).GetField(name, Flags).GetValue(chat);
    public static string Inspect()
    {
        var chat = UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        return new JObject {
            ["sessionId"] = Get<string>(chat, "sessionId"),
            ["connected"] = Get<bool>(chat, "connected"),
            ["sending"] = Get<bool>(chat, "sending"),
            ["frame"] = Time.frameCount,
            ["permission"] = Get<JObject>(chat, "pendingPermission"),
            ["messages"] = new JArray(Get<ScrollRect>(chat, "conversation").content.GetComponentsInChildren<TMP_Text>().Select(t => t.text).Reverse().Take(2).Reverse())
        }.ToString();
    }
    public static string Send(string prompt)
    {
        var chat = UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        if (!Application.isPlaying || !Get<bool>(chat, "connected") || Get<bool>(chat, "sending"))
            throw new InvalidOperationException("Chat must be connected and idle in Play Mode.");
        Get<TMP_InputField>(chat, "input").text = prompt;
        Get<Button>(chat, "sendButton").onClick.Invoke();
        return Inspect();
    }
    public static async Task<string> Record()
    {
        var chat = UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        var session = Get<string>(chat, "sessionId");
        var items = new JArray();
        string cursor = null;
        for (var page = 0; ; page++)
        {
            if (page == 20) throw new InvalidOperationException("Export pagination limit exceeded.");
            var query = cursor == null ? "?order=asc&limit=100" : "?limit=100&cursor=" + Uri.EscapeDataString(cursor);
            var response = await Get<JsonHttpClient>(chat, "http").RequestAsync("GET",
                "/api/session/" + session + "/message" + query, null, CancellationToken.None);
            var batch = (JArray)response["data"];
            foreach (var item in batch) items.Add(item);
            var next = (string)response["cursor"]?["next"];
            if (batch.Count == 0 || string.IsNullOrEmpty(next)) break;
            if (next == cursor) throw new InvalidOperationException("Export cursor did not advance.");
            cursor = next;
        }
        var messages = new JArray();
        foreach (var message in items.OfType<JObject>())
        {
            if (!new[] { "user", "assistant", "idle" }.Contains((string)message["type"])) continue;
            var copy = (JObject)message.DeepClone();
            if (copy["content"] is JArray content)
                copy["content"] = new JArray(content.OfType<JObject>().Where(p =>
                    (string)p["type"] == "text" || ((string)p["type"] == "tool" && ((string)p["name"]).StartsWith("seyra_"))));
            messages.Add(copy);
        }
        var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/conversation-" + session + ".json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, new JObject { ["sessionId"] = session, ["messages"] = messages }.ToString());
        return path;
    }
    public static string Decide(string decision)
    {
        var chat = UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        var permission = Get<JObject>(chat, "pendingPermission");
        if ((string)permission?["action"] != "read" || !(permission["resources"] is JArray resources) ||
            resources.Count != 1 || (string)resources[0] != "README.md" || (decision != "once" && decision != "reject"))
            throw new InvalidOperationException("Only the deliberate read-permission fixture may be answered.");
        var button = Get<Button>(chat, decision == "once" ? "allowPermissionButton" : "rejectPermissionButton");
        if (!button.interactable) throw new InvalidOperationException("Permission decision is not available.");
        button.onClick.Invoke();
        button.onClick.Invoke();
        return Inspect();
    }
}
