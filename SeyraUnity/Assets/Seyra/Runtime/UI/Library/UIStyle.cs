using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Seyra.UI.Library
{
    /// <summary>Semantic presentation only. Native Selectables still own all input and navigation.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class UIStyle : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [SerializeField] SeyraUITheme defaultTheme;
        [SerializeField] UITextRole textRole;
        [SerializeField] UIInkRole inkRole;
        [SerializeField] UISurfaceRole surfaceRole;
        [SerializeField] UISpacing paddingRole, spacingRole;
        [SerializeField] UISizeRole sizeRole;
        [SerializeField, HideInInspector] Material originalSurfaceMaterial;
        [SerializeField, HideInInspector] bool controlsSurfaceMaterial;
        SeyraUITheme boundTheme;
        bool pending;
        public SeyraUITheme Theme => boundTheme != null ? boundTheme : defaultTheme;
        public UIInkRole InkRole { get => inkRole; set { inkRole = value; RefreshTheme(); } }
        public void UseFallbackTheme(SeyraUITheme theme) { defaultTheme = theme; RefreshTheme(); }
        public void SetTypography(UITextRole role, UIInkRole ink) { textRole = role; inkRole = ink; RefreshTheme(); }

        public void Configure(SeyraUITheme theme, UITextRole text = UITextRole.None, UIInkRole ink = UIInkRole.Text,
            UISurfaceRole surface = UISurfaceRole.None, UISpacing padding = UISpacing.None,
            UISpacing spacing = UISpacing.None, UISizeRole size = UISizeRole.None)
        {
            defaultTheme = theme; textRole = text; inkRole = ink; surfaceRole = surface;
            paddingRole = padding; spacingRole = spacing; sizeRole = size;
            RefreshTheme();
        }

        void OnEnable() => RefreshTheme();
        void OnDisable() { if (boundTheme != null) boundTheme.Changed -= Changed; }
        void OnValidate() => pending = true;
        void OnTransformParentChanged() => pending = true;
        void Changed() => pending = true;
        void LateUpdate() { if (pending) { pending = false; RefreshTheme(); } }

        public void RefreshTheme()
        {
            var next = defaultTheme;
            for (var ancestor = transform; ancestor != null; ancestor = ancestor.parent)
            {
                var scope = ancestor.GetComponent<UIThemeScope>();
                if (scope == null || scope.Theme == null) continue;
                next = scope.Theme; break;
            }
            if (boundTheme != null) boundTheme.Changed -= Changed;
            boundTheme = next;
            if (boundTheme == null) return;
            if (isActiveAndEnabled) boundTheme.Changed += Changed;
            var theme = boundTheme;
            var manager = GetComponentInParent<Oculus.Interaction.UIThemeManager>();
            var meta = manager != null && manager.Themes != null && manager.CurrentThemeIndex >= 0 && manager.CurrentThemeIndex < manager.Themes.Length
                ? manager.Themes[manager.CurrentThemeIndex] : null;
            var text = GetComponent<TMP_Text>();
            if (text != null && textRole != UITextRole.None)
            {
                if (theme.font != null) text.font = theme.font;
                text.fontSize = theme.FontSize(textRole);
                text.fontStyle = textRole == UITextRole.Heading || textRole == UITextRole.Label ? FontStyles.Bold : FontStyles.Normal;
                text.color = theme.Ink(inkRole);
                if (meta != null)
                {
                    text.font = meta.textFontMedium;
                    text.color = inkRole == UIInkRole.Muted ? meta.textSecondaryColor : meta.textPrimaryColor;
                }
            }
            var image = GetComponent<UnityEngine.UI.Image>();
            var selectable = GetComponent<UnityEngine.UI.Selectable>();
            if (image != null && surfaceRole != UISurfaceRole.None && (selectable == null || selectable.targetGraphic == image))
                image.color = selectable != null ? Color.white : theme.Surface(surfaceRole);
            if (image != null && selectable == null)
            {
                var gradient = meta != null && meta.ThemeVersion >= 2 &&
                    (surfaceRole == UISurfaceRole.Surface || surfaceRole == UISurfaceRole.Background)
                    ? meta.backplateGradientMaterial : null;
                if (gradient != null)
                {
                    if (!controlsSurfaceMaterial) originalSurfaceMaterial = image.material;
                    image.material = gradient; controlsSurfaceMaterial = true;
                }
                else if (controlsSurfaceMaterial)
                {
                    image.material = originalSurfaceMaterial;
                    originalSurfaceMaterial = null; controlsSurfaceMaterial = false;
                }
                if (meta != null && surfaceRole != UISurfaceRole.None)
                    image.color = surfaceRole == UISurfaceRole.Subtle ? meta.sectionPlateColor : meta.backplateColor;
            }
            if (selectable != null && surfaceRole != UISurfaceRole.None)
            {
                var colors = selectable.colors;
                var normal = theme.Surface(surfaceRole);
                colors.normalColor = normal;
                colors.highlightedColor = Color.Lerp(normal, theme.focus, .18f);
                colors.pressedColor = Color.Lerp(normal, Color.black, .16f);
                colors.selectedColor = Color.Lerp(normal, theme.focus, .25f);
                colors.disabledColor = theme.disabled;
                colors.colorMultiplier = 1;
                selectable.colors = colors;
                var input = selectable as TMP_InputField;
                if (input != null) { input.customCaretColor = true; input.caretColor = theme.primary; input.selectionColor = theme.secondary; }
            }
            var group = GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
            if (group != null)
            {
                var padding = theme.Space(paddingRole);
                group.padding = new RectOffset(padding, padding, padding, padding);
                group.spacing = theme.Space(spacingRole);
            }
            var element = GetComponent<UnityEngine.UI.LayoutElement>();
            if (element != null && (sizeRole == UISizeRole.Control || sizeRole == UISizeRole.Icon || sizeRole == UISizeRole.ScrollRegion))
            {
                if (sizeRole == UISizeRole.Icon)
                {
                    element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = theme.iconSize;
                    element.flexibleWidth = element.flexibleHeight = 0;
                }
                else
                {
                    element.minHeight = theme.controlHeight;
                    // Buttons/toggles must also accommodate their label and description layouts.
                    // TMP inputs need an explicit preference because their text viewport is internally scrolled.
                    element.preferredHeight = sizeRole == UISizeRole.ScrollRegion ? theme.scrollRegionHeight : selectable is TMP_InputField ? theme.controlHeight : -1;
                }
            }
            var rect = (RectTransform)transform;
            if (sizeRole == UISizeRole.DropdownPopup) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, theme.scrollRegionHeight);
            // TMP takes over the generated popup content once its options have been instantiated.
            if (sizeRole == UISizeRole.DropdownContent && transform.childCount <= 1) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, theme.controlHeight);
            if (sizeRole == UISizeRole.SliderHandle) rect.sizeDelta = new Vector2(theme.controlHeight, rect.sizeDelta.y);
            var outline = GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) ApplyFocus(UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == gameObject);
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

        void ApplyFocus(bool selected)
        {
            var outline = GetComponent<UnityEngine.UI.Outline>();
            var theme = Theme;
            if (outline == null || theme == null) return;
            outline.enabled = selected || surfaceRole == UISurfaceRole.Field;
            outline.effectColor = selected ? theme.focus : theme.border;
            outline.effectDistance = Vector2.one * (selected ? theme.focusThickness : theme.borderThickness);
        }
        public void OnSelect(BaseEventData data) => ApplyFocus(true);
        public void OnDeselect(BaseEventData data) => ApplyFocus(false);
    }
}
