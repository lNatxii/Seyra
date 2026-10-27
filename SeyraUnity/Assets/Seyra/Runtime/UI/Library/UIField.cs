using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed class UIField : MonoBehaviour
    {
        [SerializeField] TMP_Text label, help;
        [SerializeField] UnityEngine.UI.Selectable control;
        public UnityEngine.UI.Selectable Control => control;
        public string Label { get => label.text; set { label.text = value; label.enabled = !string.IsNullOrEmpty(value); } }
        public bool Interactable { get => control.interactable; set => control.interactable = value; }
        public void SetHelp(string value, bool error = false)
        {
            if (help == null) return;
            help.text = value;
            help.enabled = !string.IsNullOrEmpty(value);
            (help.transform.parent == transform ? help.gameObject : help.transform.parent.gameObject).SetActive(!string.IsNullOrEmpty(value));
            var style = help.GetComponent<UIStyle>();
            if (style != null) style.InkRole = error ? UIInkRole.Danger : UIInkRole.Muted;
        }
        public void Configure(TMP_Text text, TMP_Text supportingText, UnityEngine.UI.Selectable selectable)
        { label = text; help = supportingText; control = selectable; }
    }
}
