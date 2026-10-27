using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    public sealed partial class UIFactory
    {
        public RectTransform ContentOf(string id) => Get(id).Content ?? throw new InvalidOperationException("The instance does not expose content.");
        public RectTransform ActionsOf(string id) => Get(id).Actions ?? throw new InvalidOperationException("The instance does not expose actions.");
        public void SetVisible(string id, bool visible) => Get(id).gameObject.SetActive(visible);
        public void SetInteraction(string id, bool enabled)
        {
            var item = Get(id); var group = Ensure<CanvasGroup>(item);
            group.interactable = group.blocksRaycasts = enabled;
        }
        public void SetOpacity(string id, float alpha)
        {
            if (!Finite(alpha) || alpha < 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
            Ensure<CanvasGroup>(Get(id)).alpha = alpha;
        }
        public void SetTheme(string id, SeyraUITheme theme)
        {
            Ensure<UIThemeScope>(Get(id)).Theme = theme;
        }
        public void SetLayout(string id, UILayoutOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options)); options.Validate();
            var item = Get(id); var element = item.GetComponent<UnityEngine.UI.LayoutElement>();
            ValidatePlacement((RectTransform)item.transform.parent, item.Placement, options.IgnoreLayout ?? element.ignoreLayout);
            options.Apply(element);
        }
        public void SetAnchors(string id, UIAnchorRect anchors)
        {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            var item = Get(id); var parent = (RectTransform)item.transform.parent;
            ValidatePlacement(parent, anchors, item.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout);
            anchors.Apply(item.Root); item.RememberPlacement(anchors);
        }
        public void Reparent(string id, RectTransform parent, int siblingIndex = -1)
        {
            var item = Get(id); ValidateParent(parent);
            if (parent == item.Root || parent.IsChildOf(item.Root)) throw new ArgumentException("An element cannot be its own descendant.");
            if (siblingIndex < -1 || siblingIndex > parent.childCount) throw new ArgumentOutOfRangeException(nameof(siblingIndex));
            ValidatePlacement(parent, item.Placement, item.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout);
            item.Root.SetParent(parent, false);
            ApplyPlacement(item.Root, parent, item.Placement, item.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout);
            if (siblingIndex >= 0) item.Root.SetSiblingIndex(siblingIndex);
            foreach (var style in item.GetComponentsInChildren<UIStyle>(true)) style.RefreshTheme();
        }
        public bool Focus(string id)
        {
            var item = Get(id);
            var control = item.ViewRoot.GetComponentInChildren<UnityEngine.UI.Selectable>();
            if (control == null || !control.IsActive() || !control.IsInteractable() || UnityEngine.EventSystems.EventSystem.current == null) return false;
            control.Select(); if (control is TMP_InputField input) input.ActivateInputField(); return true;
        }
        public void ScrollTo(string id, Vector2 normalized)
        {
            if (!Finite(normalized.x) || !Finite(normalized.y) || normalized.x < 0 || normalized.x > 1 || normalized.y < 0 || normalized.y > 1)
                throw new ArgumentOutOfRangeException(nameof(normalized));
            var scroll = Required<UnityEngine.UI.ScrollRect>(Get(id));
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content); scroll.StopMovement(); scroll.normalizedPosition = normalized;
        }
        public void RebuildLayout() { if (root != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root); }
        public void Remove(string id)
        {
            var item = Get(id);
            ValidateRemoval(item);
            foreach (var child in item.GetComponentsInChildren<UIInstance>(true))
                if (child.Owner == this) { Forget(child); child.Retire(); }
            Dispose(item.gameObject);
        }
        void ValidateRemoval(UIInstance item)
        {
            var subtree = item.GetComponentsInChildren<UIInstance>(true);
            var embedded = new System.Collections.Generic.HashSet<UIFactory>(subtree.Where(i => i.Owner == this).SelectMany(i => i.EmbeddedFactories));
            if (item.GetComponentsInChildren<UIFactory>(true).Any(f => f != this && !embedded.Contains(f)) ||
                subtree.Any(i => !i.Retired && i.Owner != this && !embedded.Contains(i.Owner)))
                throw new InvalidOperationException("The subtree contains UI owned by another factory.");
        }
        public void Clear()
        {
            var owned = instances.Values.Where(i => i != null && !i.Retired).ToArray();
            // Preflight the entire operation: rejection must not leave a partially cleared UI.
            foreach (var item in owned) ValidateRemoval(item);
            foreach (var item in owned)
                if (item != null && TryGet(item.Id, out _)) Remove(item.Id);
        }
    }
}
