using System.Linq;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed class UIActionButton : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Button button;
        [SerializeField] TMP_Text label;
        [SerializeField] UnityEngine.UI.Image icon;
        [SerializeField] GameObject iconContainer;
        public UnityEngine.UI.Button Button => button;
        public UnityEngine.UI.Button.ButtonClickedEvent OnClick => button.onClick;
        public string Label { get => label.text; set => label.text = value; }
        public bool Interactable { get => button.interactable; set => button.interactable = value; }
        public RectTransform IconTransform => icon.rectTransform;
        public void SetIcon(Sprite value)
        {
            icon.sprite = value; iconContainer.SetActive(value != null);
            var gap = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Gap");
            if (gap != null) gap.gameObject.SetActive(value != null);
        }
        public void Configure(UnityEngine.UI.Button control, TMP_Text text, UnityEngine.UI.Image image, GameObject container)
        { button = control; label = text; icon = image; iconContainer = container; }
    }
}
