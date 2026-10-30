using Seyra.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Seyra.Voice
{
    /// <summary>
    /// Push-to-talk button for XR: hold while speaking, releases to transcribe into a chat draft.
    /// Uses native Meta Voice SDK dictation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PushToTalkButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] SeyraSpatialChatWindow chat;
        [SerializeField] VoiceDictation dictation;
        [SerializeField] TMP_Text label;
        [SerializeField] Image background;
        [SerializeField] Color idleColor = new Color(0.22f, 0.28f, 0.38f, 1f);
        [SerializeField] Color listeningColor = new Color(0.85f, 0.25f, 0.25f, 1f);
        [SerializeField] Color processingColor = new Color(0.85f, 0.65f, 0.15f, 1f);
        [SerializeField] Color unavailableColor = new Color(0.20f, 0.22f, 0.25f, 0.6f);

        bool pressed;
        string notice = "";

        public VoiceDictation Dictation => dictation;
        public bool IsPressed => pressed;

        public void Bind(SeyraSpatialChatWindow chatWindow, VoiceDictation voiceDictation)
        {
            if (dictation != null) UnbindEvents();
            chat = chatWindow;
            dictation = voiceDictation;
            if (dictation != null && isActiveAndEnabled)
            {
                BindEvents();
                Refresh();
            }
        }

        void Awake()
        {
            if (chat == null) chat = GetComponentInParent<SeyraSpatialChatWindow>();
            if (dictation == null) dictation = GetComponentInParent<VoiceDictation>();
            if (background == null) background = GetComponent<Image>();
        }

        void OnEnable()
        {
            BindEvents();
            Refresh();
        }

        void OnDisable()
        {
            pressed = false;
            if (dictation != null && dictation.Busy) dictation.Cancel();
            chat?.CancelDictationPreview();
            UnbindEvents();
        }

        void BindEvents()
        {
            if (dictation == null) return;
            dictation.Changed += Refresh;
            dictation.Final += OnFinalTranscription;
            dictation.Failed += OnDictationFailed;
            dictation.Ended += OnDictationEnded;
            dictation.Partial += OnPartialTranscription;
        }

        void UnbindEvents()
        {
            if (dictation == null) return;
            dictation.Changed -= Refresh;
            dictation.Final -= OnFinalTranscription;
            dictation.Failed -= OnDictationFailed;
            dictation.Ended -= OnDictationEnded;
            dictation.Partial -= OnPartialTranscription;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (pressed || (eventData != null && eventData.button != PointerEventData.InputButton.Left)) return;
            StartDictation();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed || (eventData != null && eventData.button != PointerEventData.InputButton.Left)) return;
            StopDictation();
        }

        public void SimulatePress(bool down)
        {
            if (down) StartDictation();
            else StopDictation();
        }

        void StartDictation()
        {
            notice = "";
            if (dictation == null)
            {
                notice = "The dictation service is not configured.";
                chat?.SetVoiceHint(notice);
                Refresh();
                return;
            }

            if (!dictation.Available || dictation.Busy)
            {
                Refresh();
                return;
            }

            pressed = true;
            chat?.BeginDictationPreview();
            dictation.Begin();
            if (!dictation.Busy) chat?.CancelDictationPreview();
            Refresh();
        }

        void StopDictation()
        {
            pressed = false;
            if (dictation != null)
                dictation.End();
            Refresh();
        }

        void OnPartialTranscription(string text)
        {
            if (chat != null)
            {
                chat.UpdateDictationPreview(text);
                chat.SetVoiceHint(VoiceDictation.ListeningText);
            }
        }

        void OnFinalTranscription(string text)
        {
            if (chat != null)
            {
                chat.CommitDictation(text);
                notice = "Review the text and press Send";
                if (!chat.CanSend)
                    notice += chat.IsConnected
                        ? " · Send will be available when the turn ends"
                        : " · Connect to OpenCode to send";
                chat.SetVoiceHint(notice);
            }
            Refresh();
        }

        void OnDictationFailed(string message)
        {
            chat?.CancelDictationPreview();
            notice = "Voice: " + message;
            chat?.SetVoiceHint(notice);
            Refresh();
        }

        void OnDictationEnded() => chat?.CancelDictationPreview();

        void Update()
        {
            // Empty results and service cancellation have no Final event.
            if (dictation != null && !dictation.Busy) chat?.CancelDictationPreview();
        }

        void Refresh()
        {
            if (dictation != null && dictation.Busy)
            {
                chat?.SetVoiceHint(dictation.Status);
            }

            if (background != null)
            {
                if (dictation == null || !dictation.Available)
                    background.color = unavailableColor;
                else if (dictation.Listening)
                    background.color = listeningColor;
                else if (dictation.Busy)
                    background.color = processingColor;
                else
                    background.color = idleColor;
            }

            if (label != null)
            {
                if (dictation == null || !dictation.Available)
                    label.text = "Mic";
                else if (dictation.Listening)
                    label.text = "Listening…";
                else if (dictation.Busy)
                    label.text = "Transcribing…";
                else
                    label.text = "Talk";
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            pressed = false;
            dictation?.Cancel();
            chat?.CancelDictationPreview();
            Refresh();
        }
    }
}
