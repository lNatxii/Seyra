using System.Collections.Generic;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    public static partial class UIPresentationRenderers
    {
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
                v.Factory.InstantiateInteractButton(card, new UIButtonOptions { Label = "Ampliar / restablecer imagen" }, () =>
                {
                    ApplyZoom(zoom == 1 ? 2 : 1);
                    v.Record("image.zoom");
                });
            }
            else Text(v, "Imagen no disponible: " + image.imageRef + (image.alt.Length > 0 ? "\n" + image.alt : ""), picture, UITextRole.Help);
            var caption = Column(v, card, "CaptionContainer");
            Text(v, image.title, caption, UITextRole.Label); Text(v, image.description, caption);
            if (image.alt.Length > 0) Text(v, image.alt, caption, UITextRole.Help, UIInkRole.Muted);
        }
        static void Pager(UIPresentationView v, List<RectTransform> pages, string noun)
        {
            var status = Text(v, "", v.Actions, UITextRole.Help);
            // Empty text is normally omitted; pager status is always present.
            if (status == null) status = v.Factory.InstantiateText("1", v.Actions, new UITextOptions { Role = UITextRole.Help });
            var previous = v.Factory.InstantiateCancelButton(v.Actions, new UIButtonOptions { Label = "Anterior" });
            var next = v.Factory.InstantiateAcceptButton(v.Actions, new UIButtonOptions { Label = "Siguiente" });
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
            Pager(v, pages, "Imagen");
        }
        static void Guide(UIPresentationView v, StepGuideData d)
        {
            if (d.prerequisites.Length > 0)
            {
                var prior = Card(v, v.Body, "Antes de empezar");
                foreach (var item in d.prerequisites) Text(v, "• " + item, prior);
            }
            var pages = new List<RectTransform>();
            for (var i = 0; i < d.steps.Length; i++)
            {
                var step = d.steps[i]; var page = Card(v, v.Body, $"{i + 1}. {step.title}", step.description);
                Text(v, step.notice, page, UITextRole.Label);
                foreach (var image in step.images) Photo(v, image, page);
                v.Values[step.id] = "false";
                var control = v.Factory.InstantiateToggle(page, new UIToggleOptions { Label = "Paso completado" }, on => { v.Values[step.id] = on ? "true" : "false"; v.Record("step.checked"); });
                v.BindValue(step.id, value => ((UnityEngine.UI.Toggle)control.Control).SetIsOnWithoutNotify(value == "true"));
                pages.Add(page);
            }
            Pager(v, pages, "Paso");
        }
        static void Documents(UIPresentationView v, DocumentViewerData d)
        {
            var pages = new List<RectTransform>();
            foreach (var document in d.documents)
            {
                var page = Column(v, v.Body);
                Photo(v, new UIPhotoData { imageRef = document.imageRef, title = document.title, description = document.description, alt = document.alt }, page);
                if (document.legend.Length > 0) Card(v, page, "Leyenda", document.legend);
                for (var i = 0; i < document.annotations.Length; i++) Text(v, $"{i + 1}. {document.annotations[i]}", page);
                pages.Add(page);
            }
            Pager(v, pages, "Documento");
        }
    }
}
