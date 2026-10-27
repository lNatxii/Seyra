using System;
using Seyra.SceneTools;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.UI
{
    /// <summary>
    /// Persistent spatial menu / dock providing fast, accessible control to toggle the chat window,
    /// recenter it in front of the user's head pose, and monitor connection status.
    /// Preserves conversation session without duplicating controllers or connections.
    /// </summary>
    public sealed class SeyraSpatialMenu : MonoBehaviour
    {
        [Header("Target Spatial Chat")]
        [SerializeField] SeyraSpatialChatWindow chatWindow;
        [SerializeField] UIXRWindow chatXrWindow;
        [SerializeField] SeyraLazyFollow follow;

        [Header("Controls")]
        [SerializeField] Button toggleChatButton;
        [SerializeField] TMP_Text toggleChatLabel;
        [SerializeField] Button recenterButton;
        [SerializeField] Button voiceActionButton;

        [Header("Status Indicators")]
        [SerializeField] Image chatStatusDot;
        [SerializeField] Image runtimeStatusDot;
        [SerializeField] TMP_Text statusSummary;

        [Header("Recenter Settings")]
        [SerializeField] float defaultDistance = 1.2f;
        [SerializeField] float heightOffset = -0.05f;

        float lastStatusCheck;

        public SeyraSpatialChatWindow ChatWindow
        {
            get => chatWindow;
            set => chatWindow = value;
        }

        public UIXRWindow ChatXrWindow
        {
            get => chatXrWindow;
            set => chatXrWindow = value;
        }

        void Awake()
        {
            if (follow == null) follow = GetComponent<SeyraLazyFollow>();
            if (toggleChatButton != null) toggleChatButton.onClick.AddListener(OnToggleChatClicked);
            if (recenterButton != null) recenterButton.onClick.AddListener(OnRecenterClicked);
            if (voiceActionButton != null) voiceActionButton.onClick.AddListener(OnVoiceActionClicked);
        }

        void OnEnable()
        {
            RefreshUI();
        }

        void Update()
        {
            if (Time.unscaledTime - lastStatusCheck >= 1.0f)
            {
                lastStatusCheck = Time.unscaledTime;
                RefreshUI();
            }
        }

        public void Configure(SeyraSpatialChatWindow window, UIXRWindow xrWindow)
        {
            chatWindow = window;
            chatXrWindow = xrWindow;
            RefreshUI();
        }

        public void OnToggleChatClicked()
        {
            if (chatXrWindow != null)
            {
                if (chatXrWindow.gameObject.activeSelf)
                {
                    chatXrWindow.Close();
                }
                else
                {
                    chatXrWindow.Open();
                    if (chatWindow != null) chatWindow.ScrollToBottom();
                }
            }
            else if (chatWindow != null)
            {
                chatWindow.gameObject.SetActive(!chatWindow.gameObject.activeSelf);
                if (chatWindow.gameObject.activeSelf) chatWindow.ScrollToBottom();
            }
            RefreshUI();
        }

        public void OnRecenterClicked()
        {
            if (follow == null) follow = GetComponent<SeyraLazyFollow>();
            var head = SeyraLazyFollow.ResolveHead(follow != null ? follow.Head : null);
            if (head == null || !float.IsFinite(head.position.sqrMagnitude)) return;
            if (follow != null)
            {
                follow.ResetOffset();
                follow.SnapToTarget();
            }
            var target = chatXrWindow != null ? chatXrWindow.transform : (chatWindow != null ? chatWindow.transform : null);
            if (target == null) return;

            // Compute forward vector projected onto the horizontal plane to keep window upright
            var forward = Quaternion.Euler(0, head.eulerAngles.y, 0) * Vector3.forward;

            var newPos = head.position + forward * defaultDistance;
            newPos.y = head.position.y + heightOffset;

            target.position = newPos;
            target.rotation = Quaternion.LookRotation(forward, Vector3.up);

            // If window was closed, open it upon recentering
            if (chatXrWindow != null && !chatXrWindow.gameObject.activeSelf)
            {
                chatXrWindow.Open();
                if (chatWindow != null) chatWindow.ScrollToBottom();
            }
            else if (chatWindow != null && !chatWindow.gameObject.activeSelf)
            {
                chatWindow.gameObject.SetActive(true);
                chatWindow.ScrollToBottom();
            }

            RefreshUI();
        }

        public void OnVoiceActionClicked()
        {
            if (chatWindow != null && chatWindow.PushToTalk != null)
            {
                // If chat is hidden, open it
                if (chatXrWindow != null && !chatXrWindow.gameObject.activeSelf)
                {
                    chatXrWindow.Open();
                }
                chatWindow.ScrollToBottom();
            }
        }

        public void RefreshUI()
        {
            bool isChatOpen = (chatXrWindow != null && chatXrWindow.gameObject.activeSelf) ||
                              (chatXrWindow == null && chatWindow != null && chatWindow.gameObject.activeSelf);

            if (toggleChatLabel != null)
                toggleChatLabel.text = isChatOpen ? "Ocultar chat" : "Mostrar chat";

            // Status checks
            bool isChatConnected = chatWindow != null && chatWindow.Session != null && chatWindow.Session.IsConnected;
            if (chatStatusDot != null)
                chatStatusDot.color = isChatConnected ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.95f, 0.25f, 0.3f);

            var runtime = FindFirstObjectByType<SeyraSceneRuntime>();
            bool isRuntimeConnected = runtime != null && runtime.IsBridgeConnected;
            if (runtimeStatusDot != null)
                runtimeStatusDot.color = isRuntimeConnected ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.6f, 0.65f, 0.7f);

            if (statusSummary != null)
            {
                string chatTxt = isChatConnected ? "Chat: Conectado" : "Chat: Desconectado";
                string runTxt = isRuntimeConnected ? "Runtime: OK" : "Runtime: Inactivo";
                statusSummary.text = $"{chatTxt} · {runTxt}";
            }
        }
    }
}
