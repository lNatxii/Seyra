using System.Collections.Generic;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    public static partial class UIPresentationRenderers
    {
        static void MediaCard(UIPresentationView v, MediaCardData d)
        {
            var header = v.Factory.InstantiateContainer(v.Content, new UIElementOptions {
                Name = "MediaHeader", Layout = new UILayoutOptions { PreferredHeight = 0, FlexibleHeight = 0 }
            });
            v.Scroll.transform.SetSiblingIndex(0);
            var headerSlot = v.Factory.InstanceOf(header);
            headerSlot.transform.SetSiblingIndex(0);
            var size = headerSlot.GetComponent<UnityEngine.UI.LayoutElement>();
            size.minHeight = 0; size.flexibleHeight = 0;
            var background = header.gameObject.AddComponent<UnityEngine.UI.Image>(); background.raycastTarget = false;
            header.gameObject.AddComponent<UIStyle>().Configure(v.Factory.Catalog.DefaultTheme, surface: UISurfaceRole.Surface);
            v.ResizeMediaHeader();
            if (v.TryMedia(d.image.imageRef, out var sprite))
            {
                var go = new GameObject("Image", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.AspectRatioFitter));
                go.transform.SetParent(header, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = go.GetComponent<UnityEngine.UI.Image>(); graphic.sprite = sprite; graphic.preserveAspect = true; graphic.raycastTarget = false;
                var aspect = go.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            else if (v.IsMediaPending(d.image.imageRef)) Text(v, "Creating image…", header, UITextRole.Help);
            else Text(v, "Image unavailable" + (d.image.alt.Length > 0 ? "\n" + d.image.alt : ""), header, UITextRole.Help);
            if (d.image.source.Length > 0) Text(v, "Source: " + d.image.source, v.Body, UITextRole.Help);
            Text(v, d.description, v.Body, UITextRole.Label);
            Text(v, string.Join(" · ", d.tags), v.Body, UITextRole.Help);
            Text(v, d.body, v.Body);
            foreach (var fact in d.facts) Text(v, fact.label + ": " + fact.value, v.Body);
        }
        static void Photo(UIPresentationView v, UIPhotoData image, RectTransform parent)
        {
            var photoKey = v.RegisterPhoto();
            var card = Column(v, parent, "MediaCard");
            var picture = v.Factory.InstantiateContainer(card, new UIElementOptions { Name = "ImageContainer", Layout = new UILayoutOptions { PreferredHeight = v.Factory.Catalog.DefaultTheme.scrollRegionHeight } });
            if (v.TryMedia(image.imageRef, out var sprite))
            {
                var go = new GameObject("Image", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.AspectRatioFitter));
                go.transform.SetParent(picture, false);
                var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = go.GetComponent<UnityEngine.UI.Image>(); graphic.sprite = sprite; graphic.preserveAspect = true; graphic.raycastTarget = false;
                var aspect = go.GetComponent<UnityEngine.UI.AspectRatioFitter>(); aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = sprite.rect.width / sprite.rect.height;
                var zoom = 1;
                void ApplyZoom(int value)
                {
                    zoom = value;
                    v.Factory.SetLayout(v.Factory.InstanceOf(picture).Id, new UILayoutOptions { PreferredHeight = v.Factory.Catalog.DefaultTheme.scrollRegionHeight * zoom });
                    v.SetZoom(photoKey, zoom);
                }
                v.BindZoom(photoKey, ApplyZoom);
                v.Factory.InstantiateInteractButton(card, new UIButtonOptions { Label = "Enlarge / reset image" }, () =>
                {
                    ApplyZoom(zoom == 1 ? 2 : 1);
                    v.Record("image.zoom");
                });
            }
            else Text(v, "Image unavailable" + (image.imageRef.StartsWith("web:", System.StringComparison.Ordinal) ? "" : ": " + image.imageRef) +
                (image.alt.Length > 0 ? "\n" + image.alt : ""), picture, UITextRole.Help);
            var caption = Column(v, card, "CaptionContainer");
            if (image.source.Length > 0) Text(v, "Source: " + image.source, caption, UITextRole.Help);
            Text(v, image.title, caption, UITextRole.Label); Text(v, image.description, caption);
            if (image.alt.Length > 0) Text(v, image.alt, caption, UITextRole.Help, UIInkRole.Muted);
        }
        static void Pager(UIPresentationView v, List<RectTransform> pages, string noun)
        {
            var status = Text(v, "", v.Actions, UITextRole.Help);
            // Empty text is normally omitted; pager status is always present.
            if (status == null) status = v.Factory.InstantiateText("1", v.Actions, new UITextOptions { Role = UITextRole.Help });
            var previous = v.Factory.InstantiateCancelButton(v.Actions, new UIButtonOptions { Label = "Previous" });
            var next = v.Factory.InstantiateAcceptButton(v.Actions, new UIButtonOptions { Label = "Next" });
            void Show(int index, bool record)
            {
                v.SetIndex(index, pages); status.text = $"{noun} {index + 1} / {pages.Count}";
                previous.Interactable = index > 0; next.Interactable = index + 1 < pages.Count;
                if (record) v.Record("page.changed");
            }
            previous.OnClick.AddListener(() => Show(v.Index - 1, true));
            next.OnClick.AddListener(() => Show(v.Index + 1, true)); Show(0, false);
            v.RestorePage = index => Show(index, false);
        }
        static void Gallery(UIPresentationView v, GalleryData d)
        {
            var pages = new List<RectTransform>();
            foreach (var image in d.images) { var page = Column(v, v.Body); Photo(v, image, page); pages.Add(page); }
            Pager(v, pages, "Image");
        }
        static void Guide(UIPresentationView v, StepGuideData d)
        {
            if (d.prerequisites.Length > 0)
            {
                var prior = Card(v, v.Body, "Before you start");
                foreach (var item in d.prerequisites) Text(v, "• " + item, prior);
            }
            var pages = new List<RectTransform>();
            for (var i = 0; i < d.steps.Length; i++)
            {
                var step = d.steps[i]; var page = Card(v, v.Body, $"{i + 1}. {step.title}", step.description);
                Text(v, step.notice, page, UITextRole.Label);
                foreach (var image in step.images) Photo(v, image, page);
                v.Values[step.id] = "false";
                var control = v.Factory.InstantiateToggle(page, new UIToggleOptions { Label = "Step completed" }, on => { v.Values[step.id] = on ? "true" : "false"; v.Record("step.checked"); });
                v.BindValue(step.id, value => ((UnityEngine.UI.Toggle)control.Control).SetIsOnWithoutNotify(value == "true"));
                pages.Add(page);
            }
            Pager(v, pages, "Step");
        }
        static void Documents(UIPresentationView v, DocumentViewerData d)
        {
            var pages = new List<RectTransform>();
            foreach (var document in d.documents)
            {
                var page = Column(v, v.Body);
                Photo(v, new UIPhotoData { imageRef = document.imageRef, title = document.title, description = document.description, alt = document.alt }, page);
                if (document.legend.Length > 0) Card(v, page, "Legend", document.legend);
                for (var i = 0; i < document.annotations.Length; i++) Text(v, $"{i + 1}. {document.annotations[i]}", page);
                pages.Add(page);
            }
            Pager(v, pages, "Document");
        }
    }
}
