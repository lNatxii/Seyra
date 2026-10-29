using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Seyra.UI.Library
{
    [RequireComponent(typeof(UnityEngine.UI.ScrollRect)), DisallowMultipleComponent]
    public sealed class UIScrollIndicator : UIBehaviour
    {
        UnityEngine.UI.ScrollRect scroll;
        SeyraUITheme theme;
        UnityEngine.UI.Image track, thumb;
        UIScrollIndicatorFade fade;
        TextMeshProUGUI arrow;
        UIScrollIndicatorSizeObserver contentObserver, viewportObserver;

        public static UIScrollIndicator Ensure(UnityEngine.UI.ScrollRect scroll, SeyraUITheme theme)
        {
            if (scroll == null) return null;
            var indicator = scroll.GetComponent<UIScrollIndicator>() ?? scroll.gameObject.AddComponent<UIScrollIndicator>();
            indicator.Configure(scroll, theme);
            return indicator;
        }

        void Configure(UnityEngine.UI.ScrollRect target, SeyraUITheme nextTheme)
        {
            Unsubscribe(); scroll = target; theme = nextTheme;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            if (track == null)
            {
                track = Visual<UnityEngine.UI.Image>("ScrollIndicatorTrack", viewport);
                var rect = track.rectTransform;
                rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(-10, 4); rect.offsetMax = new Vector2(-4, -4);
                thumb = Visual<UnityEngine.UI.Image>("ScrollIndicatorThumb", rect);
                thumb.rectTransform.anchorMin = new Vector2(0, 1); thumb.rectTransform.anchorMax = Vector2.one;
                thumb.rectTransform.pivot = new Vector2(.5f, 1);
                fade = Visual<UIScrollIndicatorFade>("ScrollIndicatorFade", viewport);
                fade.rectTransform.anchorMin = Vector2.zero; fade.rectTransform.anchorMax = Vector2.right;
                fade.rectTransform.pivot = new Vector2(.5f, 0);
                fade.rectTransform.sizeDelta = new Vector2(0, 48); fade.rectTransform.anchoredPosition = Vector2.zero;
                arrow = Visual<TextMeshProUGUI>("ScrollIndicatorArrow", fade.rectTransform);
                arrow.rectTransform.anchorMin = Vector2.zero; arrow.rectTransform.anchorMax = Vector2.one;
                arrow.rectTransform.offsetMin = arrow.rectTransform.offsetMax = Vector2.zero;
                arrow.text = "▾"; arrow.richText = false; arrow.alignment = TextAlignmentOptions.Center;
            }
            contentObserver = Observe(scroll.content);
            viewportObserver = Observe(viewport);
            scroll.onValueChanged.AddListener(Scrolled);
            if (theme != null) theme.Changed += ApplyTheme;
            ApplyTheme();
        }

        UIScrollIndicatorSizeObserver Observe(RectTransform rect)
        {
            if (rect == null) return null;
            var observer = rect.GetComponent<UIScrollIndicatorSizeObserver>() ?? rect.gameObject.AddComponent<UIScrollIndicatorSizeObserver>();
            observer.Changed += Refresh;
            return observer;
        }
        static T Visual<T>(string name, RectTransform parent) where T : UnityEngine.UI.Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
            go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            var graphic = go.AddComponent<T>(); graphic.raycastTarget = false;
            return graphic;
        }
        void ApplyTheme()
        {
            if (track == null) return;
            var ink = theme != null ? theme.Ink(UIInkRole.Muted) : Color.gray;
            track.color = new Color(ink.r, ink.g, ink.b, .25f);
            thumb.color = new Color(ink.r, ink.g, ink.b, .8f);
            var surface = theme != null ? theme.Surface(UISurfaceRole.Surface) : Color.white;
            fade.color = new Color(surface.r, surface.g, surface.b, .9f);
            arrow.color = ink; arrow.fontSize = theme != null ? theme.FontSize(UITextRole.Help) : 16;
            if (theme != null && theme.font != null) arrow.font = theme.font;
            Refresh();
        }
        void Scrolled(Vector2 value) => Refresh();
        protected override void OnEnable() { base.OnEnable(); Refresh(); }
        protected override void OnDisable()
        {
            base.OnDisable();
            if (track != null) track.gameObject.SetActive(false);
            if (fade != null) fade.gameObject.SetActive(false);
        }
        void Refresh()
        {
            if (!isActiveAndEnabled || scroll == null || track == null) return;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float height = viewport.rect.height, contentHeight = scroll.content != null ? scroll.content.rect.height : 0;
            bool visible = scroll.vertical && height > 0 && contentHeight > height + 1;
            track.gameObject.SetActive(visible);
            fade.gameObject.SetActive(visible && scroll.verticalNormalizedPosition > .01f);
            if (!visible) return;
            float trackHeight = Mathf.Max(0, height - 8);
            float thumbHeight = Mathf.Min(trackHeight, Mathf.Max(24, height * height / contentHeight));
            thumb.rectTransform.sizeDelta = new Vector2(0, thumbHeight);
            thumb.rectTransform.anchoredPosition = new Vector2(0, -(trackHeight - thumbHeight) * (1 - Mathf.Clamp01(scroll.verticalNormalizedPosition)));
        }
        void Unsubscribe()
        {
            if (scroll != null) scroll.onValueChanged.RemoveListener(Scrolled);
            if (theme != null) theme.Changed -= ApplyTheme;
            if (contentObserver != null) contentObserver.Changed -= Refresh;
            if (viewportObserver != null) viewportObserver.Changed -= Refresh;
        }
        protected override void OnDestroy() { Unsubscribe(); base.OnDestroy(); }
    }

    // RectTransform notifications avoid a polling Update on every scroll view.
    public sealed class UIScrollIndicatorSizeObserver : UIBehaviour
    {
        public event Action Changed;
        protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); Changed?.Invoke(); }
    }

    public sealed class UIScrollIndicatorFade : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            var top = color; top.a = 0;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.right);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), top, Vector2.one);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), top, Vector2.up);
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
        }
    }
}
