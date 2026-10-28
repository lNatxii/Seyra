using Seyra.SceneTools;
using Seyra.Spatial;
using Seyra.UI.Library;
using Seyra.Voice;
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
        [SerializeField] Button toggleChatButton, voiceActionButton;
        [FormerlySerializedAs("recenterButton")]
        [SerializeField] Button bringChatButton;
        [SerializeField] Image chatStatusDot, runtimeStatusDot;
        [SerializeField] TMP_Text statusSummary;
        [SerializeField] Image toggleChatIcon, voiceIcon;
        [SerializeField] Sprite showChatSprite, hideChatSprite, microphoneSprite, microphoneOffSprite;
        string bringChatStatus = "";
        float lastStatusCheck;
        VoiceDictation subscribedDictation;

        public SeyraSpatialChatWindow ChatWindow { get => chatWindow; set => chatWindow = value; }
        public UIXRWindow ChatXrWindow { get => chatXrWindow; set => chatXrWindow = value; }
        public string StatusText => statusSummary != null ? statusSummary.text : "";

        void Awake()
        {
            if (spatialFollow == null) spatialFollow = GetComponentInParent<SpatialFollowController>();
            if (toggleChatButton != null) toggleChatButton.onClick.AddListener(OnToggleChatClicked);
            if (voiceActionButton != null) voiceActionButton.onClick.AddListener(OnVoiceActionClicked);
            if (bringChatButton != null) bringChatButton.onClick.AddListener(OnBringChatClicked);
        }
        void OnEnable() { SubscribeDictation(); RefreshUI(); }
        void OnDisable()
        {
            if (subscribedDictation != null) subscribedDictation.Changed -= RefreshUI;
            subscribedDictation = null;
        }
        void SubscribeDictation()
        {
            var dictation = chatWindow != null ? chatWindow.Dictation : null;
            if (dictation == subscribedDictation) return;
            if (subscribedDictation != null) subscribedDictation.Changed -= RefreshUI;
            subscribedDictation = dictation;
            if (subscribedDictation != null) subscribedDictation.Changed += RefreshUI;
        }
        void Update()
        {
            if (Time.unscaledTime - lastStatusCheck < .1f) return;
            lastStatusCheck = Time.unscaledTime;
            RefreshUI();
        }
        public void Configure(SeyraSpatialChatWindow window, UIXRWindow xrWindow)
        { chatWindow = window; chatXrWindow = xrWindow; if (isActiveAndEnabled) SubscribeDictation(); RefreshUI(); }

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
            if (spatialFollow == null) bringChatStatus = "Seguimiento suspendido";
            else
            {
                spatialFollow.ResumeFollow();
                spatialFollow.Recenter(out bringChatStatus);
            }
            // Check interaction before opening: scroll handlers record activity on disable.
            OpenChat();
            RefreshUI();
        }
        public void OnVoiceActionClicked()
        {
            OpenChat();
            var voice = chatWindow != null ? chatWindow.PushToTalk : null;
            if (voice != null) voice.SimulatePress(!voice.IsPressed);
            RefreshUI();
        }
        public void RefreshUI()
        {
            bool open = chatXrWindow != null ? chatXrWindow.gameObject.activeSelf : chatWindow != null && chatWindow.gameObject.activeSelf;
            if (toggleChatIcon != null) toggleChatIcon.sprite = open ? hideChatSprite : showChatSprite;
            var dictation = chatWindow != null ? chatWindow.Dictation : null;
            bool listening = dictation != null && dictation.Listening;
            if (voiceIcon != null) voiceIcon.sprite = listening ? microphoneOffSprite : microphoneSprite;
            if (voiceActionButton != null) voiceActionButton.interactable = dictation == null || !dictation.Busy || listening;
            bool connected = chatWindow != null && chatWindow.Session != null && chatWindow.Session.IsConnected;
            if (chatStatusDot != null) chatStatusDot.color = connected ? new Color(.2f, .85f, .45f) : new Color(.95f, .25f, .3f);
            var runtime = FindFirstObjectByType<SeyraSceneRuntime>();
            if (runtimeStatusDot != null) runtimeStatusDot.color = runtime != null && runtime.IsBridgeConnected
                ? new Color(.2f, .85f, .45f) : new Color(.6f, .65f, .7f);
            if (statusSummary != null) statusSummary.text = !string.IsNullOrEmpty(bringChatStatus) ? bringChatStatus :
                listening ? VoiceDictation.ListeningText : connected ? "Chat conectado" : "Chat sin conexión";
        }
    }
}
