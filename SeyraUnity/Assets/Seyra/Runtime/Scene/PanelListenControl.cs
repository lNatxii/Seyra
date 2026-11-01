using Seyra.UI.Library;
using Seyra.Voice;
using UnityEngine;

namespace Seyra.SceneTools
{
    /// <summary>Lives on the window, surviving view rebuilds and stopping when the window is retired.</summary>
    public sealed class PanelListenControl : MonoBehaviour
    {
        [SerializeField] ManagedPanel panel;
        [SerializeField] UIActionButton button;
        [SerializeField] Sprite listenIcon, stopIcon;
        SpokenReplies output;
        string readingKey;

        public Transform ButtonTransform => button != null ? button.transform : null;

        public void Configure(ManagedPanel owner, UIPrefabCatalog catalog)
        { panel = owner; listenIcon = catalog.ListenIcon; stopIcon = catalog.StopIcon; }

        public void Bind(UIActionButton next)
        {
            if (button != null) button.Button.onClick.RemoveListener(Toggle);
            button = next;
            if (button != null) button.Button.onClick.AddListener(Toggle);
            Refresh();
        }

        void Toggle()
        {
            if (output != null && output.IsReading(this)) { output.Stop(); return; }
            var voice = SpokenReplies.Active;
            if (voice == null) { Show("Voice failed", listenIcon); return; }
            Subscribe(voice);
            readingKey = panel.SpeechKey;
            voice.SpeakContent(this, SpeechText.PrepareContent(panel.SpokenText()));
            Refresh();
        }

        void Subscribe(SpokenReplies voice)
        {
            Unsubscribe();
            output = voice;
            output.Changed += Refresh;
        }

        void Unsubscribe()
        { if (output != null) output.Changed -= Refresh; }

        void Refresh()
        {
            var reading = output != null && output.IsReading(this);
            var failed = output != null && output.FailedOwner == this;
            Show(reading ? "Stop" : failed ? "Voice failed" : "Listen", reading ? stopIcon : listenIcon);
            if (!reading && !failed) Unsubscribe();
        }

        void Show(string label, Sprite icon)
        { if (button != null) { button.Label = label; button.SetIcon(icon); } }

        public void ContentMayHaveChanged()
        { if (output != null && output.IsReading(this) && readingKey != panel.SpeechKey) output.Stop(); }

        void OnEnable() => Refresh();
        void OnDisable()
        {
            if (output != null && output.IsReading(this)) output.Stop();
            Unsubscribe();
        }
    }
}
