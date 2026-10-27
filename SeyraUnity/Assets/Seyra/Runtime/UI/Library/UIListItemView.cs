using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed class UIListItemView : MonoBehaviour
    {
        [SerializeField] UIPanelView panel;
        [SerializeField] UnityEngine.UI.Image icon;
        [SerializeField] GameObject iconContainer;
        [SerializeField] UIActionButton action;
        public UIPanelView Panel => panel;
        public UIActionButton Action => action;
        public void SetIcon(Sprite value) { icon.sprite = value; iconContainer.SetActive(value != null); }
        public void ShowAction(bool visible) => panel.Actions.gameObject.SetActive(visible);
        public void Configure(UIPanelView view, UnityEngine.UI.Image image, GameObject container, UIActionButton button)
        { panel = view; icon = image; iconContainer = container; action = button; }
    }
}
