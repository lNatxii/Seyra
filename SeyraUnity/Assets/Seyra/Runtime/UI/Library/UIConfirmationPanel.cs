using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    public sealed class UIConfirmationPanel : MonoBehaviour
    {
        [SerializeField] UIActionButton accept, cancel;
        [SerializeField] TMPro.TMP_Text message;
        [SerializeField] bool closeOnDecision;
        [SerializeField] UnityEvent accepted = new UnityEvent(), cancelled = new UnityEvent();
        public UnityEvent Accepted => accepted;
        public UnityEvent Cancelled => cancelled;
        public UIActionButton AcceptButton => accept;
        public UIActionButton CancelButton => cancel;
        public string Message { get => message != null ? message.text : ""; set { if (message != null) message.text = value; } }
        public bool CloseOnDecision { get => closeOnDecision; set => closeOnDecision = value; }
        void OnEnable() { if (accept != null) accept.OnClick.AddListener(Accept); if (cancel != null) cancel.OnClick.AddListener(Cancel); }
        void OnDisable() { if (accept != null) accept.OnClick.RemoveListener(Accept); if (cancel != null) cancel.OnClick.RemoveListener(Cancel); }
        public void Accept() { accepted.Invoke(); Close(); }
        public void Cancel() { cancelled.Invoke(); Close(); }
        void Close()
        {
            if (!closeOnDecision) return;
            var instance = GetComponentInParent<UIInstance>();
            if (instance != null && instance.ViewRoot == transform) instance.gameObject.SetActive(false);
            else gameObject.SetActive(false);
        }
        public void Configure(UIActionButton primary, UIActionButton secondary, TMPro.TMP_Text messageText = null)
        { accept = primary; cancel = secondary; message = messageText; }
    }
}
