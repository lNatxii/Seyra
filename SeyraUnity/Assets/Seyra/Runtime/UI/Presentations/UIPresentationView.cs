using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    /// <summary>Prefab adapter. Renderers fill slots; local controls own paging, focus and edits.</summary>
    public sealed partial class UIPresentationView : MonoBehaviour
    {
        [SerializeField] string kind;
        [SerializeField] UIPanelView panel;
        [SerializeField] UnityEngine.UI.ScrollRect scroll;
        [SerializeField] UIFactory factory;
        public string Kind => kind;
        public UIPresentationData Data { get; private set; }
        public UIFactory Factory => factory;
        public RectTransform Body => scroll.content;
        internal RectTransform Content => panel.Content;
        internal UnityEngine.UI.ScrollRect Scroll => scroll;
        internal void ResizeMediaHeader()
        {
            if (!(Data is MediaCardData)) return;
            var header = panel.Content.Find("MediaHeader");
            var canvas = GetComponentInParent<Canvas>(true);
            if (header != null && canvas != null)
                header.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = ((RectTransform)canvas.transform).rect.height * .45f;
        }
        public RectTransform Actions => panel.Actions;
        public int Revision { get; private set; }
        public int Index { get; private set; }
        public string NodeId { get; set; }
        public bool Submitted { get; private set; }
        public JObject Submission { get; set; }
        public string LastAction { get; private set; } = "";
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        readonly List<string> missingMedia = new List<string>();
        IUIMediaResolver media;
        public event Action<UIPresentationView, string> ActionRequested;
        public void Configure(string type, UIPanelView frame, UnityEngine.UI.ScrollRect viewport, UIFactory composer)
        { kind = type; panel = frame; scroll = viewport; factory = composer; }
        internal void FillValidated(UIPresentationData data, UIPrefabCatalog components, IUIMediaResolver resolver, Action close)
        {
            if (Data != null || data.Type != kind) throw new InvalidOperationException("Incompatible or already populated presentation adapter.");
            Data = data; media = resolver; factory.Configure(components, (RectTransform)transform);
            UIScrollIndicator.Ensure(scroll, components.DefaultTheme);
            panel.Title = data.title; panel.Description = "";
            panel.Actions.gameObject.SetActive(true);
            panel.Actions.GetComponent<UIAdaptiveRow>().Configure(components.DefaultTheme);
            UIPresentationRenderers.Render(this, data);
            factory.InstantiateCancelButton(Actions, new UIButtonOptions { Label = "Close", Icon = components.CloseIcon }, () => { Record("close",Submitted); close(); });
            UIAssetResources.EnsureTextMaterials(gameObject);
        }
        public void SetIndex(int index, IReadOnlyList<RectTransform> pages)
        {
            if (index < 0 || index >= pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            for (var i = 0; i < pages.Count; i++) factory.InstanceOf(pages[i]).gameObject.SetActive(i == index);
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(Body);
        }
        public bool TryMedia(string reference, out Sprite sprite)
        {
            sprite = null;
            if (media != null && media.TryResolve(reference, out sprite) && sprite != null) return true;
            if (!missingMedia.Contains(reference)) missingMedia.Add(reference);
            return false;
        }
        public bool IsMediaPending(string reference) => media != null && media.IsPending(reference);
        public void Record(string action, bool submitted = false)
        {
            if (Revision < int.MaxValue) Revision++; LastAction = action; Submitted = submitted;
            if (!submitted) Submission = null;
            ActionRequested?.Invoke(this, action);
        }
        public JObject Snapshot() => new JObject
        {
            ["id"] = Data.id, ["type"] = kind, ["title"] = Data.title, ["visible"] = gameObject.activeSelf,
            ["revision"] = Revision, ["index"] = Index, ["nodeId"] = NodeId,
            ["submitted"] = Submitted, ["lastAction"] = LastAction,
            ["submission"] = Submitted ? Submission?.DeepClone() : null,
            ["values"] = JObject.FromObject(Values), ["missingMedia"] = new JArray(missingMedia)
        };
    }
}
