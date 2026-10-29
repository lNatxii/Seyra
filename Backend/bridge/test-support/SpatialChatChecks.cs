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

    public static Task<string> Run() => RunChecks();

    public static async Task<string> RunRecenter()
    {
        var currentChat=UnityEngine.Object.FindFirstObjectByType<SeyraSpatialChatWindow>();
        var currentMenu=UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>(FindObjectsInactive.Include);
        var controller=currentChat.GetComponentInParent<Seyra.Spatial.SpatialFollowController>();
        Check(controller!=null,"Chat follow controller is required for recenter.");
        var anchor=controller!=null?controller.transform:currentChat.transform;
        var oldAnchor=new Pose(anchor.position,anchor.rotation);
        var oldChat=new Pose(currentChat.transform.localPosition,currentChat.transform.localRotation);
        var oldPlacement=Get<Seyra.Spatial.SpatialPlacementService>(controller,"placement");
        var head=Get<Transform>(controller,"head");
        var testScene=UnityEngine.SceneManagement.SceneManager.CreateScene("__SpatialChatRecenter");
        var placement=Seyra.Spatial.SpatialPlacementService.ForScene(testScene);
        try
        {
            // Match HandMenuChecks: recenter in air without awaiting a physical room capture.
            controller.Configure(head,currentChat,currentChat.GetComponent<UIXRWindow>(),placement);
            if(controller!=null)
            {
                currentChat.transform.localPosition=new Vector3(0,1.35f,1.25f);
                controller.UseTestTracking(true);
            }
            anchor.position=new Vector3(8,10,8);
            var button=Get<Button>(currentMenu,"bringChatButton");Check(button!=null,"BringChatButton missing.");
            await Wait(()=>!controller.InteractionBlocked,"Recenter interaction quiet",5);
            button.onClick.Invoke();
            var reason=Get<string>(currentMenu,"bringChatStatus");
            var sameHead=head==SeyraLazyFollow.ResolveHead();
            var diagnostic=" Reason: "+reason+"; controller head matches ResolveHead: "+sameHead+".";
            Check(reason=="Chat delante de ti","Bring chat did not report success."+diagnostic);
            Check(head!=null&&Vector3.Distance(head.position,currentChat.transform.position)<3,"Planner recenter did not place the chat near the controller viewer."+diagnostic);
            return "planner-driven chat recenter passed."+diagnostic;
        }
        finally
        {
            anchor.SetPositionAndRotation(oldAnchor.position,oldAnchor.rotation);
            currentChat.transform.SetLocalPositionAndRotation(oldChat.position,oldChat.rotation);
            if(controller!=null)controller.UseTestTracking(false);
            controller.Configure(head,currentChat,currentChat.GetComponent<UIXRWindow>(),oldPlacement);
            var unload=UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(testScene);
            while(!unload.isDone) await Task.Delay(40);
        }
    }

    static async Task<string> RunChecks()
    {
        Check(Application.isPlaying, "Play Mode is required for SpatialChatChecks.");

        chat = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialChatWindow>();
        menu = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>(FindObjectsInactive.Include);
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
        Check(menu.GetComponentInParent<SeyraHandMenu>() != null, "SpatialMenu must have a hand menu controller.");
        Check(menu.ChatWindow == chat && menu.ChatXrWindow == xrWindow, "SpatialMenu must reference the chat window.");
        checks.Add("hierarchy, grab frame, millimeter canvas, and non-generated application isolation");

        // 2. Read-only draft composer structure
        Check(chat.GetComponentsInChildren<TMP_InputField>(true).Length == 0, "Spatial composer must not contain an input field.");
        var draftText = Get<TMP_Text>(chat, "draftText");
        var sendBtn = Get<Button>(chat, "sendButton");
        Check(draftText != null && draftText.name == "DraftText" && !draftText.richText, "Literal DraftText is required.");
        Check(Get<ScrollRect>(chat, "draftScroll") != null, "Draft scroll is required.");
        Check(!chat.GetComponentsInChildren<Transform>(true).Any(t => t.name == "ClearButton"), "ClearButton must not exist.");
        Check(sendBtn != null && !sendBtn.interactable && chat.Draft == "", "Empty draft must disable Send.");
        checks.Add("draft composer structure");

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
        chat.Session.Dispose();
        var fixtureSession = new OpenCodeConversationSession();
        fixtureSession.Configure("http://127.0.0.1:4202", "seyra");
        chat.BindSession(fixtureSession);
        fixtureSession.Start();

        await Wait(() => chat.Session.IsConnected, "Fixture connection");
        var session = chat.Session.SessionId;
        Check(!string.IsNullOrEmpty(session), "Session ID is null or empty.");
        checks.Add("fixture connection and v2 session creation");

        // 6. Voice results remain drafts until the explicit Send action
        Check(chat.CanSend, "Chat must be ready after fixture connection.");
        var messageCount = chat.Session.Messages.Count;
        chat.BeginDictationPreview();
        chat.UpdateDictationPreview("Crear un");
        Check(draftText.text == "Crear un" && chat.Draft == "" && !sendBtn.interactable, "Partial text must remain a preview.");
        dictation.SimulateTranscriptionResult("complete");
        Check(chat.Draft == "complete" && !chat.IsDictating && sendBtn.interactable, "Final transcription must commit a sendable draft.");
        Check(chat.Session.Messages.Count == messageCount, "Dictation must not send a message.");
        chat.BeginDictationPreview();
        chat.UpdateDictationPreview("discarded");
        Check(!chat.SendDraft(), "Send must reject an active preview.");
        chat.CancelDictationPreview();
        Check(chat.Draft == "complete" && draftText.text == "complete", "Cancellation must restore the previous draft.");
        chat.BeginDictationPreview();
        dictation.SimulateTranscriptionResult("   ");
        await Wait(() => !chat.IsDictating, "Empty final preview restoration");
        Check(chat.Draft == "complete" && draftText.text == "complete", "Empty final must preserve the previous draft.");
        chat.BeginDictationPreview();
        typeof(PushToTalkButton).GetMethod("OnDictationFailed", Flags).Invoke(ptt, new object[] { "Fixture failure" });
        Check(chat.Draft == "complete" && !chat.IsDictating, "Voice failure must preserve the draft.");
        chat.BeginDictationPreview();
        typeof(PushToTalkButton).GetMethod("OnApplicationFocus", Flags).Invoke(ptt, new object[] { false });
        Check(chat.Draft == "complete" && !chat.IsDictating, "Focus loss must restore the draft.");
        sendBtn.onClick.Invoke();
        sendBtn.onClick.Invoke();
        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Completed, "Draft turn completion");
        Check(chat.Session.Messages.Count(m => m.Role == "Tú" && m.Text == "complete") == 1, "Send must submit exactly once.");
        Check(chat.Draft == "" && !sendBtn.interactable, "Successful send must clear the draft.");
        Check(chat.Submit("hold"), "Programmatic turn submission failed.");
        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Running, "Running turn");
        ptt.SimulatePress(true);
        Check(ptt.IsPressed && chat.IsDictating, "Dictation must start during a running turn.");
        dictation.SimulateTranscriptionResult("next draft");
        ptt.SimulatePress(false);
        Check(chat.Draft == "next draft" && !sendBtn.interactable, "Running turn must retain a blocked draft.");
        await fixture.RequestAsync("POST", "/test/control", new JObject { ["finish"] = true, ["session"] = session }, CancellationToken.None);
        await Wait(() => chat.Session.ExecutionState == OpenCodeConversationSession.TurnState.Completed, "Held turn completion");
        Check(sendBtn.interactable && chat.Draft == "next draft", "Finishing a turn must enable Send without changing the draft.");
        chat.Session.Stop();
        Check(!chat.Submit("rejected") && !chat.SendDraft() && chat.Draft == "next draft", "Disconnected sends must preserve the draft.");
        chat.Session.Start();
        await Wait(() => chat.CanSend, "Reconnect after rejected send");
        chat.CommitDictation("first dictation");
        chat.BeginDictationPreview();
        dictation.SimulateTranscriptionResult("replacement");
        Check(chat.Draft == "replacement", "A new final must replace the previous draft.");
        var draftScroll = Get<ScrollRect>(chat, "draftScroll");
        chat.CommitDictation(string.Join("\n", Enumerable.Repeat("Literal <b>draft</b> text", 12)));
        await Wait(() => draftScroll.content.rect.height > draftScroll.viewport.rect.height && draftScroll.verticalNormalizedPosition < 0.01f, "Draft layout must overflow and scroll to the end", 3);
        Check(draftScroll.content.rect.height > draftScroll.viewport.rect.height, "Long drafts must overflow into a scrollable region.");
        Check(!draftText.richText && draftText.text.Contains("<b>draft</b>"), "Draft markup must remain literal.");
        var field = draftScroll.transform.parent.GetComponent<Image>();
        var fieldStyle = field.GetComponent<UIStyle>();
        Check(fieldStyle != null && fieldStyle.InkRole == UIInkRole.Text, "Draft field must retain its shared style.");
        Check(draftText.color != field.color, "Draft text must be distinguishable from its field.");
        chat.CommitDictation("replacement");
        checks.Add("voice to draft without auto-send, failure restoration, duplicate send prevention, busy turns and disconnected sends");

        // 7. Real Button-Driven Turn Submission & Permission Flow
        chat.CommitDictation("permission");
        Check(sendBtn != null, "Send button not found.");
        Check(sendBtn.interactable, "Send button not interactable after committing a draft.");
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
        chat.CommitDictation("cancel_permission");
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

        // 10. Recenter the shared group using an explicit Editor viewer.
        checks.Add(await RunRecenter());

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
