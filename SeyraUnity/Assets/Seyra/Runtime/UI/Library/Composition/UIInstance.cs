using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Owned, backgroundless layout slot. ViewRoot is the cloned prefab inside it.</summary>
    [DisallowMultipleComponent]
    public sealed class UIInstance : MonoBehaviour
    {
        [SerializeField] UIFactory owner;
        [SerializeField] string id, templateId;
        [SerializeField] UITemplateKind kind;
        [SerializeField] RectTransform viewRoot;
        [SerializeField] UIFactory[] embeddedFactories;
        [SerializeField] bool anchored;
        [SerializeField, HideInInspector] bool retired;
        [SerializeField] Vector2 anchorMin, anchorMax, anchorPivot;
        public UIFactory Owner => owner;
        public string Id => id;
        public string TemplateId => templateId;
        public UITemplateKind Kind => kind;
        public RectTransform Root => (RectTransform)transform;
        public RectTransform ViewRoot => viewRoot;
        internal bool Retired => retired;
        internal System.Collections.Generic.IEnumerable<UIFactory> EmbeddedFactories => embeddedFactories ?? System.Array.Empty<UIFactory>();
        internal void Retire() => retired = true;
        internal UIAnchorRect Placement => anchored ? new UIAnchorRect(anchorMin, anchorMax, anchorPivot) : null;
        internal void RememberPlacement(UIAnchorRect value)
        {
            anchored = value != null;
            if (value != null) { anchorMin = value.Min; anchorMax = value.Max; anchorPivot = value.Pivot; }
        }
        public T View<T>() where T : Component => viewRoot.GetComponent<T>();
        public RectTransform Content
        {
            get
            {
                var panel = View<UIPanelView>(); if (panel != null) return panel.Content;
                var foldout = View<UIFoldout>(); if (foldout != null) return foldout.Content;
                var scroll = View<UnityEngine.UI.ScrollRect>(); if (scroll != null) return scroll.content;
                return kind == UITemplateKind.Container || kind == UITemplateKind.Row || kind == UITemplateKind.Column ? viewRoot : null;
            }
        }
        public RectTransform Actions => View<UIPanelView>()?.Actions;
        internal void Initialize(UIFactory factory, string instanceId, UITemplateEntry template, RectTransform view)
        {
            owner = factory; id = instanceId; templateId = template.Id; kind = template.Kind; viewRoot = view;
            embeddedFactories = view.GetComponentsInChildren<UIFactory>(true);
        }
        void OnDestroy() { if (owner != null) owner.Forget(this); }
    }

    [System.Serializable]
    public sealed class UIInstanceDescription
    {
        public string id, templateId, parentId;
        public UITemplateKind kind;
        public bool active, hasContent, hasActions;
        internal UIInstanceDescription(UIInstance instance)
        {
            id = instance.Id; templateId = instance.TemplateId; kind = instance.Kind;
            var parent = instance.transform.parent?.GetComponentInParent<UIInstance>(true);
            parentId = parent != null && parent.Owner == instance.Owner ? parent.Id : null;
            active = instance.gameObject.activeSelf; hasContent = instance.Content != null; hasActions = instance.Actions != null;
        }
    }
}
