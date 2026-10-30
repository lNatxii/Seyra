using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Seyra.Chat;
using Seyra.SceneTools;
using Seyra.UI.Library;
using Seyra.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.UI
{
    /// <summary>
    /// Spatial conversation window for XR, presenting chat history, turn status,
    /// server permissions, activity rows, and voice draft composition via Meta SDK Voice-to-Text.
    /// Operates inside a UIXRWindow and retains its session when hidden.
    /// </summary>
    public sealed class SeyraSpatialChatWindow : MonoBehaviour
    {
        [Header("Conversation Engine")]
        [SerializeField] string defaultServerUrl = "http://127.0.0.1:4096";
        [SerializeField] string defaultAgent = "seyra";

        [Header("Header Controls")]
        [SerializeField] Image chatConnectionDot;
        [SerializeField] TMP_Text chatConnectionLabel;
        [SerializeField] Image runtimeStatusDot;
        [SerializeField] TMP_Text runtimeStatusLabel;
        [SerializeField] Button reconnectButton;
        [SerializeField] Button closeWindowButton;

        [Header("Turn Execution Status")]
        [SerializeField] GameObject executionBanner;
        [SerializeField] TMP_Text executionLabel;
        [SerializeField] Button cancelButton;
        [SerializeField] Button closeTurnButton;

        [Header("Server Permissions Panel")]
        [SerializeField] GameObject permissionPanel;
        [SerializeField] TMP_Text permissionHeading;
        [SerializeField] TMP_Text permissionDetails;
        [SerializeField] TMP_Text permissionStatus;
        [SerializeField] Button allowPermissionButton;
        [SerializeField] Button rejectPermissionButton;
        [SerializeField] ScrollRect permissionScroll;

        [Header("Conversation View")]
        [SerializeField] ScrollRect conversationScroll;
        [SerializeField] RectTransform messagesContainer;
        [SerializeField] GameObject messageRowPrefab;
        [SerializeField] SeyraUITheme presentationTheme;
        [SerializeField] Material messageSurfaceMaterial;
        [SerializeField] bool showTechnicalDetail = false;

        [Header("Composer & Controls")]
        [SerializeField] TMP_Text draftText;
        [SerializeField] ScrollRect draftScroll;
        [SerializeField] Button sendButton;
        [SerializeField] TMP_Text composerHintLabel;

        [Header("Voice Dictation & Push-to-Talk")]
        [SerializeField] VoiceDictation voiceDictation;
        [SerializeField] PushToTalkButton pushToTalkButton;

        OpenCodeConversationSession session;
        readonly Dictionary<string, GameObject> renderedRows = new Dictionary<string, GameObject>();
        bool autoScrollPending;
        float lastRuntimeCheck;

        public OpenCodeConversationSession Session => session;
        string draft = "";
        string draftBeforeDictation = "";
        string previewText = "";
        bool draftScrollPending;
        public string Draft => draft;
        public bool IsDictating { get; private set; }
        public VoiceDictation Dictation => voiceDictation;
        public PushToTalkButton PushToTalk => pushToTalkButton;
        public bool PermissionPanelOpen => permissionPanel != null && permissionPanel.activeInHierarchy;
        public bool IsConnected => session != null && session.IsConnected;
        public bool CanSend => session != null && session.CanSend;

        public void BindSession(OpenCodeConversationSession activeSession)
        {
            if (session != null) UnbindSessionEvents();
            session = activeSession;
            if (session != null)
            {
                BindSessionEvents();
                RebuildMessagesView();
                RefreshControls();
            }
        }

        public void BeginDictationPreview()
        {
            if (IsDictating) return;
            draftBeforeDictation = draft;
            previewText = draft;
            IsDictating = true;
            RefreshDraft();
        }

        public void UpdateDictationPreview(string text)
        {
            if (!IsDictating) return;
            previewText = text ?? "";
            RefreshDraft();
        }

        public void CommitDictation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { CancelDictationPreview(); return; }
            draft = text.Trim();
            IsDictating = false;
            RefreshDraft();
        }

        public void CancelDictationPreview()
        {
            if (!IsDictating) return;
            draft = draftBeforeDictation;
            IsDictating = false;
            RefreshDraft();
        }

        public bool SendDraft()
        {
            if (IsDictating || !Submit(draft)) return false;
            draft = "";
            RefreshDraft();
            return true;
        }

        void RefreshDraft()
        {
            var text = IsDictating ? previewText : draft;
            if (draftText != null)
            {
                draftText.text = string.IsNullOrEmpty(text) ? "Hold Talk to dictate" : text;
                var style = draftText.GetComponent<UIStyle>();
                if (style != null) style.InkRole = IsDictating || string.IsNullOrEmpty(text) ? UIInkRole.Muted : UIInkRole.Text;
            }
            draftScrollPending = true;
            RefreshSendButtonState();
        }

        void LateUpdate()
        {
            if (!draftScrollPending || draftScroll == null) return;
            draftScrollPending = false;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(draftScroll.content);
            draftScroll.verticalNormalizedPosition = 0f;
        }

        public void SetVoiceHint(string hint)
        {
            if (composerHintLabel != null)
            {
                composerHintLabel.text = hint ?? "";
            }
        }

        public bool Submit(string text)
        {
            if (session == null || !session.CanSend) return false;
            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return false;
            RefreshSendButtonState();
            _ = session.SendPromptAsync(trimmed);
            return true;
        }

        void Awake()
        {
            UIScrollIndicator.Ensure(conversationScroll, presentationTheme);
            UIScrollIndicator.Ensure(permissionScroll, presentationTheme);
            if (reconnectButton != null) reconnectButton.onClick.AddListener(OnReconnectClicked);
            if (closeWindowButton != null) closeWindowButton.onClick.AddListener(OnCloseWindowClicked);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
            if (closeTurnButton != null) closeTurnButton.onClick.AddListener(OnCloseTurnClicked);
            if (allowPermissionButton != null) allowPermissionButton.onClick.AddListener(OnAllowPermissionClicked);
            if (rejectPermissionButton != null) rejectPermissionButton.onClick.AddListener(OnRejectPermissionClicked);

            if (voiceDictation == null) voiceDictation = GetComponentInChildren<VoiceDictation>();
            if (pushToTalkButton == null) pushToTalkButton = GetComponentInChildren<PushToTalkButton>();
            if (pushToTalkButton != null && voiceDictation != null)
            {
                pushToTalkButton.Bind(this, voiceDictation);
            }
        }

        void OnEnable()
        {
            if (session == null)
            {
                session = new OpenCodeConversationSession();
                session.Configure(defaultServerUrl, defaultAgent);
                BindSessionEvents();
                session.Start();
            }
            RefreshControls();
            RefreshRuntimeStatus();
            RefreshDraft();
            ScrollToBottom();
        }

        void OnDestroy()
        {
            UnbindSessionEvents();
            session?.Dispose();
            session = null;
        }

        void Update()
        {
            if (Time.unscaledTime - lastRuntimeCheck >= 1.5f)
            {
                lastRuntimeCheck = Time.unscaledTime;
                RefreshRuntimeStatus();
            }

            if (autoScrollPending)
            {
                autoScrollPending = false;
                PerformScrollToBottom();
            }
        }

        void BindSessionEvents()
        {
            if (session == null) return;
            session.ConnectionChanged += OnConnectionChanged;
            session.StateChanged += OnStateChanged;
            session.MessageUpserted += OnMessageUpserted;
            session.ActivityChanged += OnActivityChanged;
            session.PermissionChanged += OnPermissionChanged;
            session.PermissionStatusChanged += OnPermissionStatusChanged;
            session.ControlsRefreshRequested += RefreshControls;
        }

        void UnbindSessionEvents()
        {
            if (session == null) return;
            session.ConnectionChanged -= OnConnectionChanged;
            session.StateChanged -= OnStateChanged;
            session.MessageUpserted -= OnMessageUpserted;
            session.ActivityChanged -= OnActivityChanged;
            session.PermissionChanged -= OnPermissionChanged;
            session.PermissionStatusChanged -= OnPermissionStatusChanged;
            session.ControlsRefreshRequested -= RefreshControls;
        }

        void OnConnectionChanged(bool connected, string label)
        {
            if (chatConnectionDot != null)
                chatConnectionDot.color = connected ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.95f, 0.25f, 0.3f);
            if (chatConnectionLabel != null)
                chatConnectionLabel.text = label;
            RefreshControls();
        }

        void OnStateChanged(OpenCodeConversationSession.TurnState state, string detail)
        {
            if (executionLabel != null)
            {
                string title;
                switch (state)
                {
                    case OpenCodeConversationSession.TurnState.Sending: title = "Sending"; break;
                    case OpenCodeConversationSession.TurnState.Running: title = "Running"; break;
                    case OpenCodeConversationSession.TurnState.WaitingForPermission: title = "Waiting for permission"; break;
                    case OpenCodeConversationSession.TurnState.Cancelling: title = "Cancelling"; break;
                    case OpenCodeConversationSession.TurnState.Completed: title = "Completed"; break;
                    case OpenCodeConversationSession.TurnState.Cancelled: title = "Interrupted"; break;
                    case OpenCodeConversationSession.TurnState.Failed: title = "Failed"; break;
                    case OpenCodeConversationSession.TurnState.Unresolved: title = "Unknown outcome"; break;
                    default: title = "Ready"; break;
                }
                executionLabel.text = $"<b>{title}</b> · {detail}";
            }
            RefreshControls();
        }

        void OnMessageUpserted(ChatMessageItem item)
        {
            RenderOrUpdateRow(item);
            MoveActivityToEnd();
            ScrollToBottom();
        }

        void RemoveRow(string key)
        {
            if (!renderedRows.TryGetValue(key, out var row)) return;
            renderedRows.Remove(key);
            if (row == null) return;
            row.SetActive(false);
            row.transform.SetParent(null, false);
            Destroy(row);
        }

        void MoveActivityToEnd()
        {
            if (renderedRows.TryGetValue("activity", out var row) && row != null)
                row.transform.SetAsLastSibling();
        }

        void OnActivityChanged(string text)
        {
            if (showTechnicalDetail || text == null)
                RemoveRow("activity");
            else
            {
                RenderOrUpdateRow(new ChatMessageItem { Key = "activity", Role = "Seyra", Text = text });
                MoveActivityToEnd();
            }
            ScrollToBottom();
        }

        void OnPermissionChanged(JObject permission)
        {
            if (permissionPanel == null) return;
            permissionPanel.SetActive(permission != null);
            if (permission == null) return;

            var action = (string)permission["action"] ?? "unknown";
            if (permissionHeading != null)
                permissionHeading.text = $"Permission requested · {action}";

            if (permissionDetails != null)
                permissionDetails.text = permission.ToString(Formatting.Indented);

            if (permissionScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                permissionScroll.verticalNormalizedPosition = 1f;
            }
            RefreshControls();
        }

        void OnPermissionStatusChanged(string statusText)
        {
            if (permissionStatus != null && !string.IsNullOrEmpty(statusText))
                permissionStatus.text = statusText;
        }

        void RefreshRuntimeStatus()
        {
            var runtime = FindFirstObjectByType<SeyraSceneRuntime>();
            bool connected = runtime != null && runtime.IsBridgeConnected;
            if (runtimeStatusDot != null)
                runtimeStatusDot.color = connected ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.6f, 0.65f, 0.7f);
            if (runtimeStatusLabel != null)
                runtimeStatusLabel.text = connected ? "Runtime: Connected" : "Runtime: Disconnected";
        }

        public void RefreshControls()
        {
            RefreshSendButtonState();
            if (reconnectButton != null) reconnectButton.interactable = session?.CanReconnect ?? true;

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(session?.HasTurn == true && session?.CanCancel == true);
                cancelButton.interactable = session?.CanCancel ?? false;
            }

            if (closeTurnButton != null)
            {
                closeTurnButton.gameObject.SetActive(session?.CanCloseTurn == true);
                closeTurnButton.interactable = session?.CanCloseTurn ?? false;
            }

            if (allowPermissionButton != null)
                allowPermissionButton.interactable = session?.CanReplyPermission ?? false;
            if (rejectPermissionButton != null)
                rejectPermissionButton.interactable = session?.CanReplyPermission ?? false;
        }

        void RefreshSendButtonState()
        {
            if (sendButton == null) return;
            var hasText = !string.IsNullOrWhiteSpace(draft);
            var canSend = session != null && session.CanSend && hasText && !IsDictating;
            sendButton.interactable = canSend;
        }

        void RebuildMessagesView()
        {
            if (messagesContainer == null) return;
            foreach (var kvp in renderedRows)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            renderedRows.Clear();

            if (session != null)
            {
                foreach (var item in session.Messages)
                {
                    RenderOrUpdateRow(item);
                }
            }
            OnActivityChanged(session?.CurrentActivity);
            ScrollToBottom();
        }

        void RenderOrUpdateRow(ChatMessageItem item)
        {
            if (messagesContainer == null || item == null) return;
            if (!showTechnicalDetail && (item.IsTool || item.IsWorkStep))
            {
                RemoveRow(item.Key);
                return;
            }

            if (renderedRows.TryGetValue(item.Key, out var rowObject) && rowObject != null)
            {
                UpdateRowContent(rowObject, item);
                return;
            }

            rowObject = CreateRowGameObject(item);
            renderedRows[item.Key] = rowObject;
            UpdateRowContent(rowObject, item);
        }

        GameObject CreateRowGameObject(ChatMessageItem item)
        {
            var isUser = item.Role == "You";
            var isTool = item.IsTool || item.Role == "Tool";
            var isError = item.Role == "System" || item.Role == "Error";

            var row = new GameObject($"Msg_{item.Role}_{item.Key}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(messagesContainer, false);

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rowLayout.spacing = 8;

            var rowElement = row.GetComponent<LayoutElement>();
            rowElement.flexibleWidth = 1;

            var bubble = new GameObject("Bubble", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            bubble.transform.SetParent(row.transform, false);

            var bubbleImage = bubble.GetComponent<Image>();
            var theme = presentationTheme;
            if (isUser)
                bubbleImage.color = theme != null ? theme.subtle : new Color(.88f, .93f, .89f);
            else if (isTool)
                bubbleImage.color = theme != null ? theme.background : new Color(.96f, .96f, .93f);
            else if (isError)
                bubbleImage.color = Color.Lerp(theme != null ? theme.surface : Color.white, theme != null ? theme.danger : Color.red, .12f);
            else
                bubbleImage.color = theme != null ? theme.surface : Color.white;
            bubbleImage.raycastTarget = false;
            if (messageSurfaceMaterial != null)
            {
                bubbleImage.material = messageSurfaceMaterial;
                bubble.AddComponent<Oculus.Interaction.Samples.RoundedBoxUIProperties>().borderRadius = Vector4.one * 24;
            }

            var bubbleLayout = bubble.GetComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(16, 16, 12, 12);
            bubbleLayout.spacing = 4;
            bubbleLayout.childControlWidth = bubbleLayout.childControlHeight = true;
            bubbleLayout.childForceExpandWidth = true;
            bubbleLayout.childForceExpandHeight = false;

            var bubbleElement = bubble.GetComponent<LayoutElement>();
            bubbleElement.preferredWidth = 560; // Max bubble width in Canvas mm
            bubbleElement.flexibleWidth = 0;

            // Sender role label
            var roleObj = new GameObject("Role", typeof(RectTransform), typeof(TextMeshProUGUI));
            roleObj.transform.SetParent(bubble.transform, false);
            var roleText = roleObj.GetComponent<TextMeshProUGUI>();
            roleText.font = theme != null ? theme.font : draftText != null ? draftText.font : TMP_Settings.defaultFontAsset;
            roleText.fontSize = theme != null ? theme.labelSize : 18;
            roleText.fontStyle = FontStyles.Bold;
            roleText.color = theme != null ? (isError ? theme.danger : theme.primary) : new Color(.1f, .34f, .29f);
            roleText.raycastTarget = false;
            roleText.richText = false;

            // Body text
            var bodyObj = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
            bodyObj.transform.SetParent(bubble.transform, false);
            var bodyText = bodyObj.GetComponent<TextMeshProUGUI>();
            bodyText.font = roleText.font;
            bodyText.fontSize = theme != null ? theme.bodySize : 18;
            bodyText.color = theme != null ? theme.text : new Color(.12f, .18f, .19f);
            bodyText.raycastTarget = false;
            bodyText.richText = false;
            bodyText.enableWordWrapping = true;
            bodyText.overflowMode = TextOverflowModes.Overflow;

            if (theme != null)
            {
                bubble.AddComponent<UIStyle>().Configure(theme, surface: isUser || isTool ? UISurfaceRole.Subtle : UISurfaceRole.Surface,
                    padding: UISpacing.Large, spacing: UISpacing.Small);
                roleObj.AddComponent<UIStyle>().Configure(theme, UITextRole.Label, isError ? UIInkRole.Danger : UIInkRole.Accent);
                bodyObj.AddComponent<UIStyle>().Configure(theme, UITextRole.Body);
            }

            return row;
        }

        void UpdateRowContent(GameObject row, ChatMessageItem item)
        {
            var roleText = row.transform.Find("Bubble/Role")?.GetComponent<TextMeshProUGUI>();
            var bodyText = row.transform.Find("Bubble/Body")?.GetComponent<TextMeshProUGUI>();

            if (roleText != null) roleText.text = item.Role;
            if (bodyText != null) bodyText.text = item.Text;
            if (item.Key == "activity" && bodyText != null)
            {
                var style = bodyText.GetComponent<UIStyle>();
                if (style == null) style = bodyText.gameObject.AddComponent<UIStyle>();
                style.Configure(presentationTheme, UITextRole.Body, UIInkRole.Muted);
                if (style.Theme == null) bodyText.color = new Color(.4f, .45f, .46f);
            }

            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)row.transform);
        }

        public void ScrollToBottom()
        {
            autoScrollPending = true;
        }

        void PerformScrollToBottom()
        {
            if (conversationScroll == null || messagesContainer == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(messagesContainer);
            conversationScroll.verticalNormalizedPosition = 0f;
        }

        // Actions
        void OnSendClicked()
        {
            SendDraft();
        }

        void OnApplicationPause(bool paused) { if (!paused && session?.CanReconnect == true) session.Reconnect(); }
        void OnReconnectClicked()
        {
            session?.Reconnect();
        }

        void OnCloseWindowClicked()
        {
            var window = GetComponentInParent<UIXRWindow>();
            if (window != null) window.Close();
            else gameObject.SetActive(false);
        }

        void OnCancelClicked()
        {
            _ = session?.CancelTurnAsync();
        }

        void OnCloseTurnClicked()
        {
            session?.CloseTurn();
        }

        void OnAllowPermissionClicked()
        {
            _ = session?.ReplyPermissionAsync(true);
        }

        void OnRejectPermissionClicked()
        {
            _ = session?.ReplyPermissionAsync(false);
        }
    }
}
