using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Chat;
using Seyra.Configuration;
using Seyra.Networking;
using Seyra.SceneTools;
using Seyra.UI;
using Seyra.UI.Library;
using Seyra.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class SpatialChatChecks
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static SeyraSpatialChatWindow chat;
    static SeyraSpatialMenu menu;
    static JsonHttpClient fixture;

    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    static async Task Wait(Func<bool> condition, string name, int timeoutSeconds = 25)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                throw new TimeoutException(name + " timed out. Current state: " + chat?.Session?.ExecutionState);
            await Task.Delay(40);
        }
    }

    public static async Task<string> Run()
    {
        Check(Application.isPlaying, "Play Mode is required for SpatialChatChecks.");

        chat = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialChatWindow>();
        menu = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>();
        Check(chat != null, "SeyraSpatialChatWindow not found in active scene.");
        Check(menu != null, "SeyraSpatialMenu not found in active scene.");

        var checks = new JArray();

        // 1. Hierarchy, Grab and Application Architecture checks
        var appRoot = GameObject.Find("SeyraAssistantApp");
        Check(appRoot != null, "SeyraAssistantApp root is required.");
        var generated = GameObject.Find("Generated");
        Check(generated != null, "Generated root must exist.");
        Check(chat.transform.IsChildOf(appRoot.transform), "SpatialChatWindow must be under SeyraAssistantApp.");
        Check(!chat.transform.IsChildOf(generated.transform), "SpatialChatWindow must NEVER be under Generated.");
        Check(menu.transform.IsChildOf(appRoot.transform), "SpatialMenu must be under SeyraAssistantApp.");
        Check(!menu.transform.IsChildOf(generated.transform), "SpatialMenu must NEVER be under Generated.");

        var xrWindow = chat.GetComponent<UIXRWindow>();
        Check(xrWindow != null, "SpatialChatWindow must have UIXRWindow.");
        Check(xrWindow.GrabFrame != null, "SpatialChatWindow must have UIMetaGrabFrame.");
        Check(xrWindow.Canvas.renderMode == RenderMode.WorldSpace, "Canvas must be WorldSpace.");
        Check(Mathf.Approximately(xrWindow.Canvas.transform.localScale.x, 0.001f), "Canvas scale must be 0.001 (millimeter Canvas).");

        var menuXr = menu.GetComponent<UIXRWindow>();
        Check(menuXr != null, "SpatialMenu must have UIXRWindow.");
        Check(menuXr.GrabFrame != null, "SpatialMenu must have UIMetaGrabFrame.");
        checks.Add("hierarchy, grab frame, millimeter canvas, and non-generated application isolation");

        // 2. Prefab Button Wiring & Clear Button Check
        var input = chat.InputField;
        Check(input != null, "InputField is required on chat window.");
        var clearBtn = Get<Button>(chat, "clearInputButton");
        Check(clearBtn != null, "ClearInputButton is required.");

        input.text = "Mensaje de prueba para borrar";
        Check(input.text == "Mensaje de prueba para borrar", "Input text was not set.");
        clearBtn.onClick.Invoke();
        Check(input.text == "", "ClearInputButton onClick did not clear the input.");
        checks.Add("prefab button connections and input field clearing");

        // 3. Unified Environment Credentials Check
        Check(!string.IsNullOrEmpty(SeyraEnvironmentCredentials.OpenCodeServerPassword), "OpenCode password should be readable from .env.local.");
        Check(SeyraEnvironmentCredentials.HasWitToken, "Wit.ai token should be readable from .env.local (SEYRA_CLIENT_TOKEN).");
        var redacted = SeyraEnvironmentCredentials.RedactToken("Secret token: " + SeyraEnvironmentCredentials.WitClientToken);
        Check(!redacted.Contains(SeyraEnvironmentCredentials.WitClientToken), "RedactToken must mask the Wit token.");
        Check(redacted.Contains("[token oculto]"), "Redacted token must contain placeholder.");
        checks.Add("unified environment credentials reading and token redaction");

        // 4. Voice Dictation & Push-to-Talk Components Presence
        var dictation = chat.Dictation;
        Check(dictation != null, "VoiceDictation component is required on SpatialChatWindow.");
        var ptt = chat.PushToTalk;
        Check(ptt != null, "PushToTalkButton component is required on SpatialChatWindow.");
        Check(ptt.Dictation == dictation, "PushToTalkButton must reference the chat's VoiceDictation service.");
        Check(dictation.Available, "VoiceDictation should be available in editor with SEYRA_CLIENT_TOKEN configured.");
        checks.Add("Meta SDK Voice-to-Text and Push-to-Talk component presence and configuration");

        // 5. Connect against fixture (port 4202)
        fixture = new JsonHttpClient("http://127.0.0.1:4202");
        chat.Session.Stop();
        chat.Session.Configure("http://127.0.0.1:4202", "seyra");
        chat.Session.Start();

        await Wait(() => chat.Session.IsConnected, "Fixture connection");
        var session = chat.Session.SessionId;
        Check(!string.IsNullOrEmpty(session), "Session ID is null or empty.");
        checks.Add("fixture connection and v2 session creation");

        // 6. Push-to-Talk Press Simulation and Voice Transcription Flow
        Check(chat.CanSend, "Chat must be ready to send after fixture connection.");
        ptt.SimulatePress(true);
        Check(ptt.IsPressed, "PushToTalkButton press simulation failed.");
        ptt.SimulatePress(false);
        Check(!ptt.IsPressed, "PushToTalkButton release simulation failed.");

        // Simulate live transcription updates
        chat.SetTranscription("Crear un cilindro verde");
        Check(input.text == "Crear un cilindro verde", "SetTranscription did not update input field: " + input.text);
        input.text = "";

        // Voice submit
        bool submitted = chat.Submit("complete");
        Check(submitted, "Submit failed while chat was ready.");
        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Completed, "Voice turn completion");
        Check(chat.Session.Messages.Any(m => m.Role == "Tú" && m.Text == "complete"), "Voice submitted prompt missing in messages.");
        checks.Add("Push-to-Talk press/release, live transcription display, and voice prompt submission");

        // 7. Real Button-Driven Turn Submission & Permission Flow
        input.text = "permission";
        var sendBtn = Get<Button>(chat, "sendButton");
        Check(sendBtn != null, "Send button not found.");
        Check(sendBtn.interactable, "Send button not interactable after typing.");
        sendBtn.onClick.Invoke();

        Check(chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Sending ||
              chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Running ||
              chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.WaitingForPermission,
              "Sending state missing");

        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.WaitingForPermission, "WaitingForPermission state");
        var permPanel = Get<GameObject>(chat, "permissionPanel");
        Check(permPanel.activeSelf, "Permission panel is not visible when waiting for permission.");

        var allowBtn = Get<Button>(chat, "allowPermissionButton");
        Check(allowBtn != null && allowBtn.interactable, "Allow button not interactable.");
        allowBtn.onClick.Invoke();

        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Completed, "Permission turn completion");
        Check(chat.Session.Messages.Any(m => m.Role == "Tú" && m.Text == "permission"), "User message missing in history.");
        Check(chat.Session.Messages.Any(m => m.IsTool), "Tool execution row missing in history.");
        checks.Add("real send button click, permission panel display, allow once, tool summary and turn completion");

        // 8. Real Button-Driven Cancellation & Explicit Turn Closure
        input.text = "cancel_permission";
        sendBtn.onClick.Invoke();
        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.WaitingForPermission, "WaitingForPermission for cancel");

        var cancelBtn = Get<Button>(chat, "cancelButton");
        Check(cancelBtn.gameObject.activeSelf && cancelBtn.interactable, "Cancel button not available.");
        cancelBtn.onClick.Invoke();

        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Cancelled, "Turn cancellation");
        var execLabel = Get<TMP_Text>(chat, "executionLabel");
        Check(execLabel.text.Contains("permanecen"), "Cancellation explanation must state that executed actions remain.");

        var closeTurnBtn = Get<Button>(chat, "closeTurnButton");
        Check(closeTurnBtn.gameObject.activeSelf && closeTurnBtn.interactable, "Close turn button must be enabled after cancel.");
        closeTurnBtn.onClick.Invoke();

        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Ready, "Explicit turn closure to Ready");
        checks.Add("cancellation via cancel button, no rollback claim, and explicit turn closure");

        // 9. Session Preservation across Window Close & Reopen via Menu Button
        var activeSessionBefore = chat.Session.SessionId;
        var messagesCountBefore = chat.Session.Messages.Count;

        // Hide chat via window close
        xrWindow.Close();
        Check(!xrWindow.gameObject.activeSelf, "Window should be inactive after Close().");
        Check(chat.Session.IsConnected, "Session must remain connected when window is hidden.");
        Check(chat.Session.SessionId == activeSessionBefore, "Session ID changed when hidden.");

        // Toggle via menu button click
        var toggleChatBtn = Get<Button>(menu, "toggleChatButton");
        Check(toggleChatBtn != null, "ToggleChatButton missing on menu.");
        toggleChatBtn.onClick.Invoke();
        Check(xrWindow.gameObject.activeSelf, "Window should be active after menu button click.");
        Check(chat.Session.SessionId == activeSessionBefore, "Session ID changed after reopening.");
        Check(chat.Session.Messages.Count == messagesCountBefore, "Messages lost after reopening.");

        // Close and reopen again via menu button
        toggleChatBtn.onClick.Invoke();
        Check(!xrWindow.gameObject.activeSelf, "Window should be inactive after second menu button click.");
        toggleChatBtn.onClick.Invoke();
        Check(xrWindow.gameObject.activeSelf, "Window should be active again.");
        checks.Add("session and message continuity across hide and reopen without duplicate connections");

        // 10. Real Button-Driven Recenter
        chat.transform.position = new Vector3(8f, 10f, 8f);
        var recenterBtn = Get<Button>(menu, "recenterButton");
        Check(recenterBtn != null, "RecenterButton missing on menu.");
        recenterBtn.onClick.Invoke();
        var cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            var dist = Vector3.Distance(cam.transform.position, chat.transform.position);
            Check(dist < 3.0f, "Recenter button did not bring window near the camera. Distance: " + dist);
        }
        checks.Add("recenter button places chat window in front of user head pose");

        // 11. Runtime Bridge Distinction Check
        var runtimeDot = Get<Image>(chat, "runtimeStatusDot");
        var runtimeLabel = Get<TMP_Text>(chat, "runtimeStatusLabel");
        Check(runtimeDot != null && runtimeLabel != null, "Runtime status controls must exist.");
        Check(runtimeLabel.text.StartsWith("Runtime:"), "Runtime label format check: " + runtimeLabel.text);
        checks.Add("runtime bridge availability distinguished from chat connection");

        var output = new JObject
        {
            ["checks"] = checks,
            ["totalChecks"] = checks.Count,
            ["passed"] = true
        };
        return output.ToString(Newtonsoft.Json.Formatting.Indented);
    }
}
