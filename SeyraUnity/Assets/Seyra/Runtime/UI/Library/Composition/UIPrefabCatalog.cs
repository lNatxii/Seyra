using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public enum UITemplateKind { Container, Row, Column, Text, Button, Panel, Confirmation, Input, Dropdown, Toggle, Slider, Foldout, ScrollView, ListItem, Example, MetaComponent }

    public static class UITemplateIds
    {
        public const string Container = "container", Row = "row", Column = "column", Text = "text";
        public const string Button = "button", PrimaryButton = "button.primary", SecondaryButton = "button.secondary", DangerButton = "button.danger";
        public const string Panel = "panel", TitledPanel = "panel.titled", Confirmation = "panel.confirmation";
        public const string Input = "input", GrowingInput = "input.growing", Dropdown = "dropdown", Toggle = "toggle", Slider = "slider";
        public const string Foldout = "foldout", ScrollView = "scroll", ListItem = "list.item", Gallery = "catalog.gallery";
    }

    [Serializable]
    public sealed class UITemplateEntry
    {
        [SerializeField] string id, label;
        [SerializeField] UITemplateKind kind;
        [SerializeField] GameObject prefab;
        public string Id => id;
        public string Label => label;
        public UITemplateKind Kind => kind;
        public GameObject Prefab => prefab;
        public UITemplateEntry(string id, string label, UITemplateKind kind, GameObject prefab)
        { this.id = id; this.label = label; this.kind = kind; this.prefab = prefab; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Each template requires an ID.");
            if (!Enum.IsDefined(typeof(UITemplateKind), kind)) throw new ArgumentException("Unknown template kind: " + id);
            if (prefab == null || !(prefab.transform is RectTransform) || prefab.scene.IsValid())
                throw new ArgumentException("The template must be a prefab with a RectTransform: " + id);
            if (prefab.GetComponent<Canvas>() != null || prefab.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length > 0)
                throw new ArgumentException("The host provides the main Canvas and EventSystem: " + id);
            bool valid;
            switch (kind)
            {
                case UITemplateKind.Row: valid = prefab.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() != null; break;
                case UITemplateKind.Column: valid = prefab.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null; break;
                case UITemplateKind.Text: valid = prefab.GetComponent<TMP_Text>() != null; break;
                case UITemplateKind.Button: valid = prefab.GetComponent<UIActionButton>()?.Button != null; break;
                case UITemplateKind.Panel: valid = prefab.GetComponent<UIPanelView>()?.Content != null; break;
                case UITemplateKind.Confirmation: valid = prefab.GetComponent<UIConfirmationPanel>()?.AcceptButton != null && prefab.GetComponent<UIConfirmationPanel>()?.CancelButton != null && prefab.GetComponent<UIPanelView>()?.Content != null; break;
                case UITemplateKind.Input: valid = prefab.GetComponent<UIField>()?.Control is TMP_InputField; break;
                case UITemplateKind.Dropdown: valid = prefab.GetComponent<UIMetaDropdown>() != null; break;
                case UITemplateKind.Toggle: valid = prefab.GetComponent<UIField>()?.Control is UnityEngine.UI.Toggle; break;
                case UITemplateKind.Slider: valid = prefab.GetComponent<UIField>()?.Control is UnityEngine.UI.Slider && prefab.GetComponent<UISliderValue>() != null; break;
                case UITemplateKind.Foldout: valid = prefab.GetComponent<UIFoldout>()?.Content != null; break;
                case UITemplateKind.ScrollView: valid = prefab.GetComponent<UnityEngine.UI.ScrollRect>()?.content != null; break;
                case UITemplateKind.ListItem: valid = prefab.GetComponent<UIListItemView>() != null; break;
                default: valid = true; break;
            }
            if (!valid) throw new ArgumentException("The template is missing its required component: " + id);
        }
    }

    [Serializable]
    public sealed class UITemplateDescription
    {
        public string id, label;
        public UITemplateKind kind;
        public bool hasContent, hasActions, isExample;
        internal UITemplateDescription(UITemplateEntry entry)
        {
            id = entry.Id; label = entry.Label; kind = entry.Kind; isExample = kind == UITemplateKind.Example;
            hasContent = entry.Prefab.GetComponent<UIPanelView>()?.Content != null || entry.Prefab.GetComponent<UIFoldout>() != null ||
                entry.Prefab.GetComponent<UnityEngine.UI.ScrollRect>() != null || kind == UITemplateKind.Container || kind == UITemplateKind.Row || kind == UITemplateKind.Column;
            hasActions = entry.Prefab.GetComponent<UIPanelView>()?.Actions != null;
        }
    }

    [CreateAssetMenu(menuName = "Seyra/UI/Prefab catalog")]
    public sealed class UIPrefabCatalog : ScriptableObject
    {
        [SerializeField] SeyraUITheme defaultTheme;
        [SerializeField] UIXRWindow windowPrefab;
        [SerializeField] Sprite closeIcon;
        [SerializeField] Sprite listenIcon, stopIcon;
        public Sprite ListenIcon => listenIcon;
        public Sprite StopIcon => stopIcon;
        public void ConfigureListenIcons(Sprite listen, Sprite stop) { listenIcon = listen; stopIcon = stop; }
        public Sprite CloseIcon => closeIcon;
        public void ConfigureCloseIcon(Sprite icon) { closeIcon = icon; }
        [SerializeField] Oculus.Interaction.UITheme[] metaThemes;
        public UIXRWindow WindowPrefab => windowPrefab;
        public IReadOnlyList<Oculus.Interaction.UITheme> MetaThemes => metaThemes;
        public void ConfigureMeta(UIXRWindow window, Oculus.Interaction.UITheme[] themes)
        { windowPrefab = window; metaThemes = themes; }
        [SerializeField] List<UITemplateEntry> entries = new List<UITemplateEntry>();
        public SeyraUITheme DefaultTheme => defaultTheme;
        public IReadOnlyList<UITemplateEntry> Entries => entries.AsReadOnly();
        public UITemplateEntry Get(string id) => entries.FirstOrDefault(e => e != null && e.Id == id) ?? throw new KeyNotFoundException("Unknown template: " + id);
        public UITemplateDescription[] Describe() => entries.Select(e => new UITemplateDescription(e)).ToArray();
        public void Validate() => ValidateEntries(entries);
        public void Configure(SeyraUITheme theme, IEnumerable<UITemplateEntry> templates)
        {
            var next = templates?.ToList() ?? throw new ArgumentNullException(nameof(templates));
            ValidateEntries(next); defaultTheme = theme; entries = next;
        }
        static void ValidateEntries(IEnumerable<UITemplateEntry> templates)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in templates)
            {
                if (entry == null) throw new ArgumentException("Null template.");
                entry.Validate();
                if (!ids.Add(entry.Id)) throw new ArgumentException("Duplicate template ID: " + entry.Id);
            }
        }
    }
}
