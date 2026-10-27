using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed class UIPanelView : MonoBehaviour
    {
        [SerializeField] RectTransform content, actions;
        [SerializeField] TMP_Text title, description;
        public RectTransform Content => content;
        public RectTransform Actions => actions;
        public string Title { get => title != null ? title.text : ""; set { if (title != null) title.text = value; } }
        public string Description { get => description != null ? description.text : ""; set { if (description != null) { description.text = value; description.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(value)); } } }
        public void Configure(RectTransform body, RectTransform actionSlot = null, TMP_Text heading = null, TMP_Text details = null)
        { content = body; actions = actionSlot; title = heading; description = details; }
    }
}
