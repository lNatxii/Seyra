using System.Linq;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Builds the gallery from the installed SDK catalog, rather than a fixed set of Seyra controls.</summary>
    public sealed class UIMetaCatalogBrowser : MonoBehaviour
    {
        [SerializeField] UIPrefabCatalog catalog;
        UIFactory factory;
        UITemplateEntry[] entries;
        RectTransform preview;
        TMP_Text heading, status;
        int index;
        UIInstance displayed;
        public void Configure(UIPrefabCatalog source) => catalog = source;
        void Start()
        {
            if (catalog == null) throw new System.InvalidOperationException("The Meta catalog is not configured.");
            factory = GetComponent<UIFactory>(); factory.Configure(catalog, (RectTransform)transform);
            entries = catalog.Entries.Where(e=>e.Kind == UITemplateKind.MetaComponent).ToArray();
            factory.InstantiateText("Meta Horizon · UI Set", options:new UITextOptions {Role=UITextRole.Title});
            heading = factory.InstantiateText("");
            var navigation = factory.InstantiateRow();
            factory.InstantiateCancelButton(navigation, new UIButtonOptions {Label="Previous"}, ()=>Show(index-1));
            factory.InstantiateAcceptButton(navigation, new UIButtonOptions {Label="Next"}, ()=>Show(index+1));
            factory.InstantiateInteractButton(navigation, new UIButtonOptions {Label="Change theme"}, ToggleTheme);
            preview = factory.InstantiateColumn(options:new UIElementOptions {Layout=UILayoutOptions.Weight(1,1)});
            status = factory.InstantiateText("Official components from the installed SDK. Try grabbing and XR interaction on the headset.", options:new UITextOptions {Role=UITextRole.Help});
            if (entries.Length > 0) Show(0);
        }
        void Show(int next)
        {
            index = (next % entries.Length + entries.Length) % entries.Length;
            if (displayed != null) factory.Remove(displayed.Id);
            heading.text = (index+1) + " / " + entries.Length + " · " + entries[index].Label;
            displayed = factory.InstantiateMetaComponent(entries[index].Id, preview);
            status.text = entries[index].Id;
            factory.RebuildLayout();
        }
        void ToggleTheme()
        {
            var themes = GetComponentInParent<Oculus.Interaction.UIThemeManager>();
            if (themes == null) return;
            themes.ApplyTheme((themes.CurrentThemeIndex+1) % themes.Themes.Length);
            foreach (var style in GetComponentsInChildren<UIStyle>(true)) style.RefreshTheme();
        }
    }
}
