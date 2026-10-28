using System;
using Seyra.SceneTools;
using Seyra.Spatial;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Seyra.UI
{
    public sealed class SeyraSpatialMenu : MonoBehaviour
    {
        [SerializeField] SeyraSpatialChatWindow chatWindow;
        [SerializeField] UIXRWindow chatXrWindow;
        [SerializeField] SpatialFollowController spatialFollow;
        [SerializeField] RoomContext roomContext;
        [SerializeField] Button toggleChatButton, voiceActionButton, scanRoomButton;
        [FormerlySerializedAs("recenterButton")]
        [SerializeField] Button bringChatButton;
        [SerializeField] TMP_Text toggleChatLabel;
        [SerializeField] Image chatStatusDot, runtimeStatusDot;
        [SerializeField] TMP_Text statusSummary;
        string roomActionStatus = "", bringChatStatus = "";
        float lastStatusCheck;
        bool scanning;

        public SeyraSpatialChatWindow ChatWindow { get => chatWindow; set => chatWindow = value; }
        public UIXRWindow ChatXrWindow { get => chatXrWindow; set => chatXrWindow = value; }
        public string StatusText => statusSummary != null ? statusSummary.text : "";

        void Awake()
        {
            if (spatialFollow == null) spatialFollow = GetComponentInParent<SpatialFollowController>();
            if (roomContext == null) roomContext = FindFirstObjectByType<RoomContext>();
            if (toggleChatButton != null) toggleChatButton.onClick.AddListener(OnToggleChatClicked);
            if (voiceActionButton != null) voiceActionButton.onClick.AddListener(OnVoiceActionClicked);
            if (bringChatButton != null) bringChatButton.onClick.AddListener(OnBringChatClicked);
            if (scanRoomButton != null) scanRoomButton.onClick.AddListener(OnScanRoomClicked);
        }
        void OnEnable() => RefreshUI();
        void Update()
        {
            if (Time.unscaledTime - lastStatusCheck < 1) return;
            lastStatusCheck = Time.unscaledTime;
            RefreshUI();
        }
        public void Configure(SeyraSpatialChatWindow window, UIXRWindow xrWindow)
        { chatWindow = window; chatXrWindow = xrWindow; RefreshUI(); }

        public void OnToggleChatClicked()
        {
            if (chatXrWindow != null)
            {
                if (chatXrWindow.gameObject.activeSelf) chatXrWindow.Close();
                else { chatXrWindow.Open(); chatWindow?.ScrollToBottom(); }
            }
            else if (chatWindow != null) chatWindow.gameObject.SetActive(!chatWindow.gameObject.activeSelf);
            RefreshUI();
        }
        void OpenChat()
        {
            if (chatXrWindow != null) chatXrWindow.Open();
            else if (chatWindow != null) chatWindow.gameObject.SetActive(true);
            chatWindow?.ScrollToBottom();
        }
        public void OnBringChatClicked()
        {
            OpenChat();
            if (spatialFollow == null) bringChatStatus = "Seguimiento suspendido";
            else
            {
                spatialFollow.ResumeFollow();
                spatialFollow.Recenter(out bringChatStatus);
            }
            RefreshUI();
        }
        public void OnVoiceActionClicked()
        {
            OpenChat();
            var voice = chatWindow != null ? chatWindow.PushToTalk : null;
            if (voice != null) voice.SimulatePress(!voice.IsPressed);
            RefreshUI();
        }
        public async void OnScanRoomClicked()
        {
            if (roomContext == null || scanRoomButton == null || !scanRoomButton.interactable) return;
            scanning = true; roomActionStatus = "Escaneando habitación…"; RefreshUI();
            try
            {
                await roomContext.LoadDeviceAsync(true);
                roomActionStatus = roomContext != null && (roomContext.Snapshot.State == RoomState.Error ||
                    roomContext.Snapshot.State == RoomState.Unavailable || roomContext.Snapshot.State == RoomState.PermissionDenied)
                    ? "No se pudo cargar la habitación" : "";
            }
            catch (Exception error)
            { roomActionStatus = "No se pudo cargar la habitación"; Debug.LogWarning("Room scan failed: " + error.Message, this); }
            finally { scanning = false; if (this != null) RefreshUI(); }
        }
        public void RefreshUI()
        {
            if (scanRoomButton != null)
                scanRoomButton.interactable = !scanning && Application.platform == RuntimePlatform.Android && roomContext != null &&
                    (roomContext.Snapshot.State == RoomState.AirOnly || roomContext.Snapshot.State == RoomState.Unavailable);
            bool open = chatXrWindow != null ? chatXrWindow.gameObject.activeSelf : chatWindow != null && chatWindow.gameObject.activeSelf;
            if (toggleChatLabel != null) toggleChatLabel.text = open ? "Cerrar chat" : "Abrir chat";
            bool connected = chatWindow != null && chatWindow.Session != null && chatWindow.Session.IsConnected;
            if (chatStatusDot != null) chatStatusDot.color = connected ? new Color(.2f, .85f, .45f) : new Color(.95f, .25f, .3f);
            var runtime = FindFirstObjectByType<SeyraSceneRuntime>();
            if (runtimeStatusDot != null) runtimeStatusDot.color = runtime != null && runtime.IsBridgeConnected
                ? new Color(.2f, .85f, .45f) : new Color(.6f, .65f, .7f);
            string room = roomContext == null ? "Habitación: sin mapa" : roomContext.Snapshot.Simulated ? "Habitación: simulada" : roomContext.Snapshot.State switch
            {
                RoomState.Ready => "Habitación: lista", RoomState.PermissionDenied => "Habitación: sin permiso",
                RoomState.Loading => "Habitación: cargando", RoomState.Suspended => "Habitación: suspendida",
                RoomState.WaitingForPermission => "Habitación: permiso pendiente", _ => "Habitación: sin mapa"
            };
            if (statusSummary != null) statusSummary.text = room +
                (!string.IsNullOrEmpty(roomActionStatus) ? " · " + roomActionStatus : !string.IsNullOrEmpty(bringChatStatus) ? " · " + bringChatStatus : "");
        }
    }
}
