using System;
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
            Refresh();
        }
        void Scrolled(Vector2 value) => Refresh();
        protected override void OnEnable() { base.OnEnable(); Refresh(); }
        void Refresh()
        {
            if (!isActiveAndEnabled || scroll == null || track == null) return;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float height = viewport.rect.height, contentHeight = scroll.content != null ? scroll.content.rect.height : 0;
            bool visible = scroll.vertical && height > 0 && contentHeight > height + 1;
            track.canvasRenderer.SetAlpha(visible ? 1 : 0);
            thumb.canvasRenderer.SetAlpha(visible ? 1 : 0);
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
}
