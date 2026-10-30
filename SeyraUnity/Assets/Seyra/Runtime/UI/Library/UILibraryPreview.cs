using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Local library demonstration. Does not register generated content or contact the backend.</summary>
    public sealed class UILibraryPreview : MonoBehaviour
    {
        [SerializeField] UIPrefabCatalog catalog;
        [SerializeField] Transform controlsAnchor, galleryAnchor;
        public UIXRWindow ControlsWindow { get; private set; }
        public UIXRWindow GalleryWindow { get; private set; }

        public void Configure(UIPrefabCatalog templates, Transform controls, Transform gallery)
        { catalog = templates; controlsAnchor = controls; galleryAnchor = gallery; }

        void Start()
        {
            ControlsWindow = UIXRWindow.Create(catalog, controlsAnchor, new Vector2(800, 700));
            GalleryWindow = UIXRWindow.Create(catalog, galleryAnchor, new Vector2(800, 700), 1);
            ControlsWindow.name = "ControlsWindow";
            GalleryWindow.name = "GalleryWindow";
            var factory = ControlsWindow.Factory;
            factory.InstantiateText("SeYRA · UI Library", options: new UITextOptions { Role = UITextRole.Title });
            factory.InstantiateText("Local controls and independent windows.");
            var status = factory.InstantiateText("Try a control to see its result.");
            factory.InstantiateAcceptButton(options: new UIButtonOptions { Label = "Test action" },
                onClick: () => status.text = "Action received.");
            factory.InstantiateToggle(options: new UIToggleOptions { Label = "Example option" },
                onChanged: value => status.text = value ? "Option enabled." : "Option disabled.");
            factory.InstantiateSlider(options: new UISliderOptions { Label = "Value", Min = 0, Max = 100, Value = 50 },
                onChanged: value => status.text = "Value: " + value.ToString("0"));
            factory.InstantiateInputField(options: new UIInputOptions { Label = "Text", Placeholder = "Type here", CharacterLimit = 256 },
                onChanged: value => status.text = "Text: " + value);
            factory.InstantiateDropdown(options: new UIDropdownOptions { Choices = new[] { "First option", "Second option", "Third option" } },
                onChanged: value => status.text = "Option: " + (value + 1));
            var actions = factory.InstantiateRow();
            factory.InstantiateCancelButton(actions, new UIButtonOptions { Label = "Show / hide catalog" },
                () => { if (GalleryWindow.gameObject.activeSelf) GalleryWindow.Close(); else GalleryWindow.Open(); });
            factory.InstantiateInteractButton(actions, new UIButtonOptions { Label = "Change theme" },
                () => ControlsWindow.SetTheme((ControlsWindow.Themes.CurrentThemeIndex + 1) % catalog.MetaThemes.Count));
            factory.InstantiateInteractButton(options: new UIButtonOptions { Label = "Resize" }, onClick: () =>
            {
                var size = ((RectTransform)ControlsWindow.Canvas.transform).rect.size;
                ControlsWindow.Resize(size.x > 800 ? new Vector2(800, 700) : new Vector2(900, 800));
            });
            GalleryWindow.Factory.Instantiate(UITemplateIds.Gallery);
            factory.RebuildLayout();
        }
    }
}
