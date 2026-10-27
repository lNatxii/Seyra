using System;
using Oculus.Interaction;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>A real UI Set backplate with its own ray/poke canvas and scoped composition API.</summary>
    public sealed class UIXRWindow : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] RectTransform content;
        [SerializeField] UIThemeManager themes;
        [SerializeField] UIFactory factory;
        [SerializeField] UIMetaGrabFrame grabFrame;
        public Canvas Canvas => canvas;
        public RectTransform Content => content;
        public UIFactory Factory => factory;
        public UIThemeManager Themes => themes;
        public UIMetaGrabFrame GrabFrame => grabFrame;

        /// <summary>Creates a window under an explicit scene anchor without creating a rig or input system.</summary>
        public static UIXRWindow Create(UIPrefabCatalog catalog, Transform anchor, Vector2 canvasUnits, int themeIndex = 0)
        {
            if (catalog == null || catalog.WindowPrefab == null) throw new ArgumentException("A catalog with a window prefab is required.", nameof(catalog));
            if (anchor == null || !anchor.gameObject.scene.IsValid()) throw new ArgumentException("A scene anchor is required.", nameof(anchor));
            if (catalog.MetaThemes == null || themeIndex < 0 || themeIndex >= catalog.MetaThemes.Count)
                throw new ArgumentOutOfRangeException(nameof(themeIndex));
            if (!Valid(canvasUnits.x) || !Valid(canvasUnits.y)) throw new ArgumentOutOfRangeException(nameof(canvasUnits));
            var window = Instantiate(catalog.WindowPrefab, anchor, false);
            try
            {
                window.Factory.Configure(catalog, window.Content);
                window.Resize(canvasUnits);
                window.SetTheme(themeIndex);
                return window;
            }
            catch
            {
                window.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(window.gameObject); else DestroyImmediate(window.gameObject);
                throw;
            }
        }

        public void ConfigureGrab(UIMetaGrabFrame frame) { grabFrame = frame; }
        public void Configure(Canvas surface, RectTransform body, UIThemeManager themeManager, UIFactory composer)
        { canvas = surface; content = body; themes = themeManager; factory = composer; }
        public void SetTheme(int index)
        {
            if (index < 0 || index >= themes.Themes.Length) throw new ArgumentOutOfRangeException(nameof(index));
            themes.ApplyTheme(index);
            foreach (var style in GetComponentsInChildren<UIStyle>(true)) style.RefreshTheme();
        }
        public void Close() => gameObject.SetActive(false);
        public void Open() => gameObject.SetActive(true);
        public void Resize(Vector2 canvasUnits)
        {
            if (!Valid(canvasUnits.x) || !Valid(canvasUnits.y)) throw new ArgumentOutOfRangeException(nameof(canvasUnits));
            ((RectTransform)canvas.transform).sizeDelta = canvasUnits;
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(content);
            if (grabFrame != null) grabFrame.RefreshGeometry();
        }
        static bool Valid(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
