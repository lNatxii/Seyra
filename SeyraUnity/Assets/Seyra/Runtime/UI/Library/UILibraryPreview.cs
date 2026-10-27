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
            factory.InstantiateText("SeYRA · Biblioteca UI", options: new UITextOptions { Role = UITextRole.Title });
            factory.InstantiateText("Controles locales y ventanas independientes.");
            var status = factory.InstantiateText("Prueba un control para ver su resultado.");
            factory.InstantiateAcceptButton(options: new UIButtonOptions { Label = "Probar acción" },
                onClick: () => status.text = "Acción recibida.");
            factory.InstantiateToggle(options: new UIToggleOptions { Label = "Opción de ejemplo" },
                onChanged: value => status.text = value ? "Opción activada." : "Opción desactivada.");
            factory.InstantiateSlider(options: new UISliderOptions { Label = "Valor", Min = 0, Max = 100, Value = 50 },
                onChanged: value => status.text = "Valor: " + value.ToString("0"));
            factory.InstantiateInputField(options: new UIInputOptions { Label = "Texto", Placeholder = "Escribe aquí", CharacterLimit = 256 },
                onChanged: value => status.text = "Texto: " + value);
            factory.InstantiateDropdown(options: new UIDropdownOptions { Choices = new[] { "Primera opción", "Segunda opción", "Tercera opción" } },
                onChanged: value => status.text = "Opción: " + (value + 1));
            var actions = factory.InstantiateRow();
            factory.InstantiateCancelButton(actions, new UIButtonOptions { Label = "Mostrar / ocultar catálogo" },
                () => { if (GalleryWindow.gameObject.activeSelf) GalleryWindow.Close(); else GalleryWindow.Open(); });
            factory.InstantiateInteractButton(actions, new UIButtonOptions { Label = "Cambiar tema" },
                () => ControlsWindow.SetTheme((ControlsWindow.Themes.CurrentThemeIndex + 1) % catalog.MetaThemes.Count));
            factory.InstantiateInteractButton(options: new UIButtonOptions { Label = "Cambiar tamaño" }, onClick: () =>
            {
                var size = ((RectTransform)ControlsWindow.Canvas.transform).rect.size;
                ControlsWindow.Resize(size.x > 800 ? new Vector2(800, 700) : new Vector2(900, 800));
            });
            GalleryWindow.Factory.Instantiate(UITemplateIds.Gallery);
            factory.RebuildLayout();
        }
    }
}
