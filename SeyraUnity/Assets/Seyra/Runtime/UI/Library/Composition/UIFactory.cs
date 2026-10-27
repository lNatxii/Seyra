using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    /// <summary>Scoped composition API. No services, reflection, hierarchy-name searches or global host creation.</summary>
    [DisallowMultipleComponent]
    public sealed partial class UIFactory : MonoBehaviour
    {
        [SerializeField] UIPrefabCatalog catalog;
        [SerializeField] RectTransform root;
        readonly Dictionary<string, UIInstance> instances = new Dictionary<string, UIInstance>(StringComparer.Ordinal);
        public UIPrefabCatalog Catalog => catalog;
        public RectTransform Root => root;

        public void Configure(UIPrefabCatalog templates, RectTransform scope)
        {
            if (templates == null) throw new ArgumentNullException(nameof(templates));
            if (scope == null || !scope.gameObject.scene.IsValid()) throw new ArgumentException("The container must belong to a scene.");
            templates.Validate();
            if (DescribeInstances().Length != 0 && (catalog != templates || root != scope))
                throw new InvalidOperationException("Clear the factory before changing its scope or catalog.");
            catalog = templates; root = scope;
        }

        void OnEnable() => Reindex();
        void Reindex()
        {
            instances.Clear();
            if (root == null) return;
            foreach (var item in root.GetComponentsInChildren<UIInstance>(true))
                if (item.Owner == this && !item.Retired && !string.IsNullOrEmpty(item.Id)) instances.Add(item.Id, item);
        }
        internal void Forget(UIInstance item)
        { if (instances.TryGetValue(item.Id, out var current) && current == item) instances.Remove(item.Id); }
        public bool TryGet(string id, out UIInstance instance)
        {
            instance = null;
            return id != null && instances.TryGetValue(id, out instance) && instance != null && !instance.Retired;
        }
        public UIInstance Get(string id) => TryGet(id, out var item) ? item : throw new KeyNotFoundException("Unknown UI instance: " + id);
        public UIInstance InstanceOf(Component view)
        {
            var instance = view != null ? view.GetComponentInParent<UIInstance>(true) : null;
            return instance != null && !instance.Retired && instance.Owner == this ? instance : throw new ArgumentException("The component does not belong to this factory.");
        }
        public UIInstanceDescription[] DescribeInstances() => instances.Values.Where(i => i != null).Select(i => new UIInstanceDescription(i)).ToArray();

        public UIInstance Instantiate(string templateId, RectTransform parent = null, UIElementOptions options = null)
            => Create(templateId, parent, options ?? new UIElementOptions(), null);

        UIInstance Create(string templateId, RectTransform parent, UIElementOptions options, Action<UIInstance> initialize)
        {
            if (catalog == null || root == null) throw new InvalidOperationException("Configure the catalog and container before creating UI.");
            var template = catalog.Get(templateId); template.Validate();
            parent = ValidateParent(parent ?? root);
            options.Layout?.Validate();
            ValidatePlacement(parent, options.Anchors, options.Layout?.IgnoreLayout ?? false);
            var id = options.Id ?? Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("The ID must not be empty.");
            if (TryGet(id, out _)) throw new ArgumentException("Duplicate instance ID: " + id);
            GameObject slot = null;
            try
            {
                slot = new GameObject(options.Name ?? template.Label, typeof(RectTransform));
                slot.SetActive(false);
                slot.transform.SetParent(parent, false);
                var rect = (RectTransform)slot.transform;
                var layout = slot.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = true;
                var element = slot.AddComponent<UnityEngine.UI.LayoutElement>();
                var sourceLayout = template.Prefab.GetComponent<UnityEngine.UI.LayoutElement>();
                element.layoutPriority = 2;
                element.flexibleWidth = sourceLayout != null && sourceLayout.flexibleWidth >= 0 ? sourceLayout.flexibleWidth : 1;
                element.flexibleHeight = sourceLayout != null && sourceLayout.flexibleHeight >= 0 ? sourceLayout.flexibleHeight : 0;
                options.Layout?.Apply(element);
                ApplyPlacement(rect, parent, options.Anchors, element.ignoreLayout);
                var view = (RectTransform)UnityEngine.Object.Instantiate(template.Prefab, rect, false).transform;
                var item = slot.AddComponent<UIInstance>(); item.Initialize(this, id, template, view);
                item.RememberPlacement(options.Anchors);
                foreach (var style in view.GetComponentsInChildren<UIStyle>(true))
                    if (catalog.DefaultTheme != null) style.UseFallbackTheme(catalog.DefaultTheme);
                if (options.Theme != null) slot.AddComponent<UIThemeScope>().Theme = options.Theme;
                initialize?.Invoke(item);
                instances[id] = item;
                slot.SetActive(options.Active);
                var metaTheme = root.GetComponentInParent<Oculus.Interaction.UIThemeManager>(true);
                if (metaTheme != null) metaTheme.ApplyCurrentTheme();
                UIAssetResources.EnsureTextMaterials(view.gameObject);
                return item;
            }
            catch { if (slot != null) Dispose(slot); throw; }
        }

        RectTransform ValidateParent(RectTransform parent)
        {
            if (root == null || parent == null || (parent != root && !parent.IsChildOf(root)))
                throw new ArgumentException("The parent must be within the factory scope.");
            var owner = parent.GetComponentInParent<UIInstance>(true);
            if (owner != null && owner.Owner != this && (owner.Root == root || owner.Root.IsChildOf(root)))
                throw new ArgumentException("The parent belongs to another factory.");
            // A nested factory explicitly owns its root even before it has spawned a first instance.
            foreach (var factory in root.gameObject.scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<UIFactory>(true)))
                if (factory != this && factory.Root != null && (parent == factory.Root || parent.IsChildOf(factory.Root)) &&
                    (factory.Root == root || factory.Root.IsChildOf(root)))
                    throw new ArgumentException("The parent is reserved by another factory.");
            return parent;
        }
        static bool Managed(RectTransform parent) => parent.GetComponents<UnityEngine.UI.LayoutGroup>().Any(group => group.enabled);
        static void ValidatePlacement(RectTransform parent, UIAnchorRect anchors, bool ignoreLayout)
        {
            anchors?.Validate();
            if (anchors != null && Managed(parent) && !ignoreLayout)
                throw new ArgumentException("A LayoutGroup controls this parent. Use LayoutElements or explicit IgnoreLayout.");
        }
        static void ApplyPlacement(RectTransform rect, RectTransform parent, UIAnchorRect anchors, bool ignoreLayout)
        {
            if (anchors != null) anchors.Apply(rect);
            else if (!Managed(parent) || ignoreLayout) new UIAnchorRect(Vector2.zero, Vector2.one).Apply(rect);
        }
        static void Dispose(GameObject target)
        {
            target.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(target); else UnityEngine.Object.DestroyImmediate(target);
        }
        static T Required<T>(UIInstance item) where T : Component
        {
            var component = item.View<T>();
            if (component == null) throw new InvalidOperationException("Missing " + typeof(T).Name + " in " + item.TemplateId);
            return component;
        }
        static T Ensure<T>(UIInstance item) where T : Component
        {
            var component = item.GetComponent<T>();
            return component != null ? component : item.gameObject.AddComponent<T>();
        }

    }
}
