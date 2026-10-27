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
    /// server permissions, activity rows, and text/voice input via Meta SDK Voice-to-Text.
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

        [Header("Composer & Controls")]
        [SerializeField] TMP_InputField inputField;
        [SerializeField] Button sendButton;
        [SerializeField] Button clearInputButton;
        [SerializeField] TMP_Text composerHintLabel;

        [Header("Voice Dictation & Push-to-Talk")]
        [SerializeField] VoiceDictation voiceDictation;
        [SerializeField] PushToTalkButton pushToTalkButton;

        OpenCodeConversationSession session;
        readonly Dictionary<string, GameObject> renderedRows = new Dictionary<string, GameObject>();
        bool autoScrollPending;
        float lastRuntimeCheck;

        public OpenCodeConversationSession Session => session;
        public TMP_InputField InputField => inputField;
        public VoiceDictation Dictation => voiceDictation;
        public PushToTalkButton PushToTalk => pushToTalkButton;
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

        public void SetTranscription(string text)
        {
            if (inputField != null)
            {
                inputField.text = text ?? "";
                RefreshSendButtonState();
            }
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
            if (inputField != null) inputField.text = "";
            RefreshSendButtonState();
            _ = session.SendPromptAsync(trimmed);
            return true;
        }

        void Awake()
        {
            if (reconnectButton != null) reconnectButton.onClick.AddListener(OnReconnectClicked);
            if (closeWindowButton != null) closeWindowButton.onClick.AddListener(OnCloseWindowClicked);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendClicked);
            if (clearInputButton != null) clearInputButton.onClick.AddListener(OnClearInputClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
            if (closeTurnButton != null) closeTurnButton.onClick.AddListener(OnCloseTurnClicked);
            if (allowPermissionButton != null) allowPermissionButton.onClick.AddListener(OnAllowPermissionClicked);
            if (rejectPermissionButton != null) rejectPermissionButton.onClick.AddListener(OnRejectPermissionClicked);

            if (inputField != null)
            {
                inputField.onSubmit.AddListener(_ => OnSendClicked());
                inputField.onValueChanged.AddListener(_ => RefreshSendButtonState());
            }

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
                    case OpenCodeConversationSession.TurnState.Sending: title = "Enviando"; break;
                    case OpenCodeConversationSession.TurnState.Running: title = "En ejecución"; break;
                    case OpenCodeConversationSession.TurnState.WaitingForPermission: title = "Esperando permiso"; break;
                    case OpenCodeConversationSession.TurnState.Cancelling: title = "Cancelando"; break;
                    case OpenCodeConversationSession.TurnState.Completed: title = "Completado"; break;
                    case OpenCodeConversationSession.TurnState.Cancelled: title = "Interrumpido"; break;
                    case OpenCodeConversationSession.TurnState.Failed: title = "Fallo"; break;
                    case OpenCodeConversationSession.TurnState.Unresolved: title = "Resultado desconocido"; break;
                    default: title = "Listo"; break;
                }
                executionLabel.text = $"<b>{title}</b> · {detail}";
            }
            RefreshControls();
        }

        void OnMessageUpserted(ChatMessageItem item)
        {
            RenderOrUpdateRow(item);
            ScrollToBottom();
        }

        void OnPermissionChanged(JObject permission)
        {
            if (permissionPanel == null) return;
            permissionPanel.SetActive(permission != null);
            if (permission == null) return;

            var action = (string)permission["action"] ?? "desconocida";
            if (permissionHeading != null)
                permissionHeading.text = $"Permiso solicitado · {action}";

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
                runtimeStatusLabel.text = connected ? "Runtime: Conectado" : "Runtime: Desconectado";
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
            var hasText = !string.IsNullOrWhiteSpace(inputField != null ? inputField.text : "");
            var canSend = session != null && session.CanSend && hasText;
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
            ScrollToBottom();
        }

        void RenderOrUpdateRow(ChatMessageItem item)
        {
            if (messagesContainer == null || item == null) return;

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
            var isUser = item.Role == "Tú";
            var isTool = item.IsTool || item.Role == "Herramienta";
            var isError = item.Role == "Sistema" || item.Role == "Error";

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
            if (isUser)
                bubbleImage.color = new Color(0.12f, 0.28f, 0.24f); // Personal teal
            else if (isTool)
                bubbleImage.color = new Color(0.16f, 0.21f, 0.26f); // Steel slate for tools
            else if (isError)
                bubbleImage.color = new Color(0.35f, 0.15f, 0.16f); // Crimson for errors
            else
                bubbleImage.color = new Color(0.15f, 0.17f, 0.20f); // Dark surface for Seyra

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
            roleText.fontSize = 15;
            roleText.fontStyle = FontStyles.Bold;
            roleText.color = isUser ? new Color(0.55f, 0.88f, 0.72f) : (isTool ? new Color(0.6f, 0.8f, 0.95f) : (isError ? new Color(0.95f, 0.6f, 0.6f) : new Color(0.85f, 0.9f, 0.95f)));
            roleText.richText = false;

            // Body text
            var bodyObj = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
            bodyObj.transform.SetParent(bubble.transform, false);
            var bodyText = bodyObj.GetComponent<TextMeshProUGUI>();
            bodyText.fontSize = 20;
            bodyText.color = new Color(0.95f, 0.96f, 0.98f);
            bodyText.richText = false;
            bodyText.enableWordWrapping = true;
            bodyText.overflowMode = TextOverflowModes.Overflow;

            return row;
        }

        void UpdateRowContent(GameObject row, ChatMessageItem item)
        {
            var roleText = row.transform.Find("Bubble/Role")?.GetComponent<TextMeshProUGUI>();
            var bodyText = row.transform.Find("Bubble/Body")?.GetComponent<TextMeshProUGUI>();

            if (roleText != null) roleText.text = item.Role;
            if (bodyText != null) bodyText.text = item.Text;

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
            if (inputField == null || session == null || !session.CanSend) return;
            var text = inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            inputField.text = "";
            RefreshSendButtonState();
            _ = session.SendPromptAsync(text);
        }

        void OnClearInputClicked()
        {
            if (inputField != null)
            {
                inputField.text = "";
                RefreshSendButtonState();
            }
        }

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
