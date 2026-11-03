using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Seyra.Chat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.UI
{
    /// <summary>
    /// Testbed UI component for OpencodeTestUI scene.
    /// Thin UI presenter that delegates 100% of network, turn execution, cancellation,
    /// and permission decisions to the shared OpenCodeConversationSession engine.
    /// </summary>
    public sealed partial class OpenCodeTestChat : MonoBehaviour
    {
        public enum TurnState
        {
            Ready,
            Sending,
            Running,
            WaitingForPermission,
            Cancelling,
            Completed,
            Cancelled,
            Failed,
            Unresolved
        }

        [Header("Backend Connection")]
        [SerializeField] string serverUrl = "http://127.0.0.1:4096";
        [SerializeField] string agent = "seyra";

        [Header("Composer & Controls")]
        [SerializeField] TMP_InputField input;
        [SerializeField] Button sendButton;
        [SerializeField] Button reconnectButton;
        [SerializeField] Image connectionIndicator;
        [SerializeField] TMP_Text connectionLabel;
        [SerializeField] Button cancelButton;
        [SerializeField] Button closeTurnButton;
        [SerializeField] TMP_Text executionLabel;

        [Header("Conversation Scroll")]
        [SerializeField] ScrollRect conversation;
        [SerializeField] TMP_Text messageTemplate;

        [Header("Permission Panel")]
        [SerializeField] GameObject permissionPanel;
        [SerializeField] TMP_Text permissionHeading;
        [SerializeField] TMP_Text permissionDetails;
        [SerializeField] TMP_Text permissionStatus;
        [SerializeField] Button allowPermissionButton;
        [SerializeField] Button rejectPermissionButton;
        [SerializeField] ScrollRect permissionScroll;

        // Core shared conversation engine
        OpenCodeConversationSession session;

        // Mirrored state fields accessed via reflection by test fixtures (ChatExecutionChecks)
        bool connected;
        string sessionId;
        bool sessionMissing;
        string activeUserId;
        string pendingText;
        TurnState executionState = TurnState.Ready;
        string executionDetail = "Write a message to get started.";
        JObject pendingPermission;

        public TurnState ExecutionState => executionState;
        public OpenCodeConversationSession Session => session;

        void Awake()
        {
            if (messageTemplate != null)
                messageTemplate.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (session == null)
            {
                session = new OpenCodeConversationSession();
            }

            // Sync server configuration in case reflection modified serverUrl or sessionId
            session.Configure(serverUrl, agent);
            if (sessionId != session.SessionId)
            {
                session.SetSessionId(sessionId);
            }

            BindSessionEvents();
            BindUIListeners();

            session.Start();
            SyncFromSession();
            RefreshControls();
        }

        void OnDisable()
        {
            UnbindUIListeners();
            UnbindSessionEvents();

            session?.Stop();
            SyncFromSession();
            ClearPermissionsUI();
        }

        void BindUIListeners()
        {
            if (sendButton != null) sendButton.onClick.AddListener(Send);
            if (reconnectButton != null) reconnectButton.onClick.AddListener(Reconnect);
            if (cancelButton != null) cancelButton.onClick.AddListener(CancelTurn);
            if (closeTurnButton != null) closeTurnButton.onClick.AddListener(CloseTurn);
            if (allowPermissionButton != null) allowPermissionButton.onClick.AddListener(AllowPermission);
            if (rejectPermissionButton != null) rejectPermissionButton.onClick.AddListener(RejectPermission);
            if (input != null) input.onSubmit.AddListener(Submit);
        }

        void UnbindUIListeners()
        {
            if (sendButton != null) sendButton.onClick.RemoveListener(Send);
            if (reconnectButton != null) reconnectButton.onClick.RemoveListener(Reconnect);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelTurn);
            if (closeTurnButton != null) closeTurnButton.onClick.RemoveListener(CloseTurn);
            if (allowPermissionButton != null) allowPermissionButton.onClick.RemoveListener(AllowPermission);
            if (rejectPermissionButton != null) rejectPermissionButton.onClick.RemoveListener(RejectPermission);
            if (input != null) input.onSubmit.RemoveListener(Submit);
        }

        void BindSessionEvents()
        {
            if (session == null) return;
            session.ConnectionChanged += OnConnectionChanged;
            session.StateChanged += OnStateChanged;
            session.PermissionChanged += OnPermissionChanged;
            session.PermissionStatusChanged += OnPermissionStatusChanged;
            session.ControlsRefreshRequested += OnControlsRefreshRequested;
            session.MessageUpserted += OnMessageUpserted;
            session.MessageRemoved += OnMessageRemoved;
            session.EchoAdopted += OnEchoAdopted;
        }

        void UnbindSessionEvents()
        {
            if (session == null) return;
            session.ConnectionChanged -= OnConnectionChanged;
            session.StateChanged -= OnStateChanged;
            session.PermissionChanged -= OnPermissionChanged;
            session.PermissionStatusChanged -= OnPermissionStatusChanged;
            session.ControlsRefreshRequested -= OnControlsRefreshRequested;
            session.MessageUpserted -= OnMessageUpserted;
            session.MessageRemoved -= OnMessageRemoved;
            session.EchoAdopted -= OnEchoAdopted;
        }

        void OnMessageRemoved(string key)
        {
            if (!messageRows.TryGetValue(key, out var row)) return;
            messageRows.Remove(key);
            if (row == null) return;
            row.gameObject.SetActive(false);
            row.transform.SetParent(null, false);
            Destroy(row.gameObject);
        }

        void SyncFromSession()
        {
            if (session == null) return;
            connected = session.IsConnected;
            sessionId = session.SessionId;
            sessionMissing = session.IsSessionMissing;
            activeUserId = session.ActiveUserId;
            pendingText = session.PendingText;
            executionState = (TurnState)session.ExecutionState;
            executionDetail = session.ExecutionDetail;
            pendingPermission = session.PendingPermission;
        }

        void OnConnectionChanged(bool isConnected, string label)
        {
            SyncFromSession();
            if (connectionIndicator != null)
                connectionIndicator.color = isConnected ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.95f, 0.25f, 0.3f);
            if (connectionLabel != null)
                connectionLabel.text = label;
            RefreshControls();
        }

        void OnStateChanged(OpenCodeConversationSession.TurnState state, string detail)
        {
            SyncFromSession();
            RefreshControls();
        }

        void OnPermissionChanged(JObject perm)
        {
            SyncFromSession();
            UpdatePermissionUI(perm);
            RefreshControls();
        }

        void OnPermissionStatusChanged(string statusText)
        {
            if (permissionStatus != null && !string.IsNullOrEmpty(statusText))
                permissionStatus.text = statusText;
        }

        void OnControlsRefreshRequested()
        {
            SyncFromSession();
            RefreshControls();
        }

        void OnEchoAdopted(string oldKey, string newKey)
        {
            AdoptEcho(oldKey, newKey);
        }

        void OnMessageUpserted(ChatMessageItem item)
        {
            UpsertMessage(item.Key, item.Role, item.Text);
        }

        void Send()
        {
            if (input == null || session == null) return;
            var text = input.text.Trim();
            if (text.Length == 0) return;
            input.text = "";
            _ = session.SendPromptAsync(text);
        }

        void Submit(string _) => Send();

        void Reconnect()
        {
            if (session != null)
            {
                // In case serverUrl changed via reflection
                session.Configure(serverUrl, agent);
                session.Reconnect();
            }
        }

        void CancelTurn()
        {
            if (session != null)
                _ = session.CancelTurnAsync();
        }

        void CloseTurn()
        {
            if (session != null)
                session.CloseTurn();
        }

        void AllowPermission()
        {
            if (session != null)
                _ = session.ReplyPermissionAsync(true);
        }

        void RejectPermission()
        {
            if (session != null)
                _ = session.ReplyPermissionAsync(false);
        }

        void ClearPermissionsUI()
        {
            pendingPermission = null;
            if (permissionPanel != null)
                permissionPanel.SetActive(false);
        }

        void UpdatePermissionUI(JObject perm)
        {
            if (permissionPanel == null) return;
            permissionPanel.SetActive(perm != null);
            if (perm == null) return;

            if (permissionHeading != null)
                permissionHeading.text = "Permission requested · " + (string)perm["action"];

            if (permissionDetails != null)
                permissionDetails.text = perm.ToString(Formatting.Indented);

            if (permissionStatus != null)
                permissionStatus.text = "Allow authorizes this request. Reject also cancels pending permissions in this session.";

            if (permissionScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                permissionScroll.verticalNormalizedPosition = 1f;
            }
        }
    }
}
