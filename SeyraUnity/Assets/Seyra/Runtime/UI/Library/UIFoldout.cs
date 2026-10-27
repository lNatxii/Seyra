using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    public sealed class UIFoldout : MonoBehaviour
    {
        [SerializeField] UIActionButton header;
        [SerializeField] GameObject body;
        [SerializeField] bool expanded = true;
        [SerializeField] UnityEvent<bool> expandedChanged = new UnityEvent<bool>();
        public bool Expanded { get => expanded; set => SetExpanded(value); }
        public UnityEvent<bool> ExpandedChanged => expandedChanged;
        public UIActionButton Header => header;
        public RectTransform Content => body != null ? body.transform as RectTransform : null;
        void OnEnable() { if (header != null) header.OnClick.AddListener(Toggle); Apply(); }
        void OnDisable() { if (header != null) header.OnClick.RemoveListener(Toggle); }
        public void Toggle() => SetExpanded(!expanded);
        public void SetExpanded(bool value, bool notify = true) { var changed = expanded != value; expanded = value; Apply(); if (changed && notify) expandedChanged.Invoke(value); }
        void Apply()
        {
            if (body != null) body.SetActive(expanded);
            if (header != null) header.IconTransform.localRotation = Quaternion.FromToRotation(Vector3.down, expanded ? Vector3.up : Vector3.down);
        }
        public void Configure(UIActionButton button, GameObject content) { header = button; body = content; Apply(); }
    }
}
