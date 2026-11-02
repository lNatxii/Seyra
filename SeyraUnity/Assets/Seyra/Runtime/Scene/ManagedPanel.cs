using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using Seyra.Spatial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.SceneTools
{
    /// <summary>One managed entity with an owned, reconstructible visual subtree.</summary>
    [DisallowMultipleComponent]
    public sealed partial class ManagedPanel : MonoBehaviour
    {
        [SerializeField] PanelContent content;
        [SerializeField] UIXRWindow window;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("presentationVisible")] bool windowVisible = true;
        public bool WindowVisible => windowVisible;
        public void SetWindowVisible(bool visible)
        {
            windowVisible = visible;
            if (window != null) window.gameObject.SetActive(visible);
            if (visible) GetComponent<SeyraFollowTarget>()?.Snap();
        }
        public void SetGrabEnabled(bool enabled)
        {
            if (window?.GrabFrame != null) window.GrabFrame.gameObject.SetActive(enabled);
        }
        [SerializeField] RectTransform surface;
        [SerializeField] TMP_Text heading, body;
        [SerializeField] ScrollRect scroll;
        [SerializeField] UIField cardCheck;
        [SerializeField] UIActionButton closeButton;
        [SerializeField] RectTransform windowActions;
        [SerializeField] PanelListenControl listen;
        [SerializeField] string structure;
        [SerializeField] TMP_FontAsset font;
        UIPrefabCatalog labelCatalog;
        RectTransform chipBackground;
        TextAlignmentOptions labelAlignment;
        Vector4 labelMargin;
        [SerializeField] Vector3 surfacePosition;
        [SerializeField] Quaternion surfaceRotation;
        internal Action<GameObject> DiscardHandler { get; set; }
        void RequestDiscard() => DiscardHandler?.Invoke(gameObject);
        public PanelContent Content => content.Copy();
        public RectTransform Surface => surface;
        public UIXRWindow Window => window;
        internal string SpokenText() => IsPresentation ? UIPresentationSpeech.Compose(presentationView) : content.title + "\n" + content.text;
        internal string SpeechKey => IsPresentation
            ? presentationJson + "#" + presentationView.Index + "#" + presentationView.NodeId
            : content.title + "\n" + content.text;

        public void Initialize(PanelContent data, UIPrefabCatalog catalog)
        {
            if (content != null) throw new InvalidOperationException("The panel has already been initialized.");
            data = data.Copy();
            if (catalog == null) throw new InvalidOperationException("Assign the UI catalog before creating panels.");
            if (data.kind == "panel")
            {
                window = UIXRWindow.Create(catalog, transform, data.CanvasSize);
                window.name = "PanelWindow";
                // Native manipulation must move the logical entity, so history observes manual changes.
                window.GrabFrame.Grabbable.InjectOptionalTargetTransform(transform);
                surface = (RectTransform)window.Canvas.transform;
                var factory = window.Factory;
                heading = factory.InstantiateText(data.title, options: new UITextOptions { Role = UITextRole.Title,
                    Layout = new UILayoutOptions { MinHeight = 0, PreferredHeight = 72, FlexibleHeight = 0 } });
                heading.maxVisibleLines = 2; heading.overflowMode = TextOverflowModes.Ellipsis;
                scroll = factory.InstantiateScrollView(options: new UIElementOptions { Layout = new UILayoutOptions {
                    MinHeight = 0, PreferredHeight = 0, FlexibleHeight = 1 } });
                var size = scroll.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 0; size.flexibleHeight = 1;
                body = factory.InstantiateText(data.text, scroll.content);
                body.overflowMode = TextOverflowModes.Overflow;
                windowActions = factory.InstantiateRow(null, new UIElementOptions { Name = "WindowActions",
                    Layout = new UILayoutOptions { MinHeight = 0, FlexibleHeight = 0 } });
                listen = window.gameObject.AddComponent<PanelListenControl>();
                listen.Configure(this, catalog);
                listen.Bind(factory.InstantiateCancelButton(windowActions, new UIButtonOptions { Label = "Listen", Icon = catalog.ListenIcon }));
                closeButton = factory.InstantiateCancelButton(windowActions, new UIButtonOptions { Label = "Close", Icon = catalog.CloseIcon }, RequestDiscard);
            }
            else
            {
                var view = new GameObject("LabelCanvas", typeof(RectTransform), typeof(Canvas));
                view.transform.SetParent(transform, false); view.transform.localScale = Vector3.one * .001f;
                view.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                surface = (RectTransform)view.transform;
                labelCatalog = catalog;
                if (data.chip) CreateChipBackground();
                var factory = view.AddComponent<UIFactory>(); factory.Configure(catalog, surface);
                body = factory.InstantiateText(data.text, options: new UITextOptions { Ink = UIInkRole.OnAction });
                labelAlignment = body.alignment;
                labelMargin = body.margin;
                body.overflowMode = TextOverflowModes.Ellipsis;
            }
            body.richText = false; body.textWrappingMode = TextWrappingModes.Normal;
            // Explicit typography is part of this content contract, independent of theme refreshes.
            body.GetComponent<UIStyle>().enabled = false;
            Apply(data);
        }

        void CreateChipBackground()
        {
            var background = new GameObject("ChipBackground", typeof(RectTransform), typeof(Image));
            chipBackground = (RectTransform)background.transform;
            chipBackground.SetParent(surface, false);
            chipBackground.SetAsFirstSibling();
            chipBackground.anchorMin = Vector2.zero; chipBackground.anchorMax = Vector2.one;
            chipBackground.offsetMin = chipBackground.offsetMax = Vector2.zero;
            var image = background.GetComponent<Image>();
            var color = labelCatalog.DefaultTheme.Ink(UIInkRole.Text); color.a = .92f;
            image.color = color; image.raycastTarget = false;
        }

        void ApplyLabelStyle(PanelContent data)
        {
            var outline = body.GetComponent<Outline>();
            var billboard = GetComponent<SeyraBillboard>();
            if (data.chip)
            {
                if (chipBackground == null) CreateChipBackground();
                if (outline != null) DestroyImmediate(outline);
                body.margin = Vector4.one * 8;
                body.alignment = TextAlignmentOptions.Center;
                if (billboard == null) billboard = gameObject.AddComponent<SeyraBillboard>();
                billboard.Snap();
            }
            else
            {
                if (chipBackground != null)
                {
                    if (billboard != null) DestroyImmediate(billboard);
                    DestroyImmediate(chipBackground.gameObject);
                }
                if (outline == null) outline = body.gameObject.AddComponent<Outline>();
                outline.effectColor = labelCatalog.DefaultTheme.Ink(UIInkRole.Text);
                outline.effectDistance = Vector2.one * labelCatalog.DefaultTheme.borderThickness;
                body.margin = labelMargin;
                body.alignment = labelAlignment;
            }
        }

        public void Apply(PanelContent data)
        {
            if (IsPresentation) throw new ArgumentException("Use ui_update to replace a presentation.");
            data = data.Copy();
            if (content != null && content.kind != data.kind) throw new ArgumentException("Panel kind cannot change during an update.");
            content = data;
            if (window != null)
            {
                window.Resize(data.CanvasSize);
                if (data.HasCheck && cardCheck == null)
                    cardCheck = window.Factory.InstantiateToggle(options: new UIToggleOptions { Label = data.checkLabel },
                        onChanged: on => content.checkCompleted = on);
                if (cardCheck != null)
                {
                    window.Factory.InstanceOf(cardCheck).gameObject.SetActive(data.HasCheck);
                    cardCheck.Label = data.checkLabel;
                    ((Toggle)cardCheck.Control).SetIsOnWithoutNotify(data.checkCompleted);
                }
                window.Factory.InstanceOf(windowActions).transform.SetAsLastSibling();
                heading.text = data.title;
                window.Factory.InstanceOf(heading).gameObject.SetActive(data.title.Length != 0);
            }
            else
            {
                surface.sizeDelta = data.CanvasSize;
                ApplyLabelStyle(data);
            }
            body.text = data.text; body.fontSize = (float)data.fontSize;
            if (listen != null) listen.ContentMayHaveChanged();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(surface);
            body.ForceMeshUpdate(true);
            if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
            structure = Structure();
            font = body.font; surfacePosition = surface.localPosition; surfaceRotation = surface.localRotation;
        }

        string Structure() => new JArray(GetComponentsInChildren<Transform>(true).Where(t => !IsRetiredPresentationTransform(t) &&
            t.GetComponent<TMP_SelectionCaret>() == null && t.GetComponent<TMP_SubMeshUI>() == null).Select(t => new JObject {
            ["id"] = t.GetInstanceID(), ["parent"] = t == transform ? 0 : t.parent.GetInstanceID(),
            ["name"] = t == transform ? "" : t.name, ["layer"] = t == transform ? 0 : t.gameObject.layer, ["tag"] = t == transform ? "" : t.tag,
            // ISDK creates these transient interaction helpers in Start or on grab; they are not authored content.
            ["components"] = new JArray(t.GetComponents<Component>().Where(c => !(c is GeneratedObject) && !(c is SeyraFollowTarget) && !(c is Seyra.Spatial.SeyraBillboard) &&
                !(c is Oculus.Interaction.InteractableTriggerBroadcaster) && !(c is Oculus.Interaction.MoveTowardsTargetProvider) &&
                !(c is Oculus.Interaction.RigidbodyKinematicLocker))
                .Select(c => c == null ? 0 : c.GetInstanceID())) })).ToString(Newtonsoft.Json.Formatting.None);

        public bool IsIntact() => IsPresentation ? PresentationIsIntact() : content != null && body != null && surface != null &&
            (content.kind != "panel" || (window != null && heading != null && heading.text == content.title &&
                window.transform.localPosition == Vector3.zero && window.transform.localRotation == Quaternion.identity && window.transform.localScale == Vector3.one)) &&
            body.text == content.text && !body.richText && Mathf.Approximately(body.fontSize, (float)content.fontSize) &&
            body.enabled && body.gameObject.activeSelf && body.font == font && body.textWrappingMode == TextWrappingModes.Normal &&
            surface.GetComponent<Canvas>() != null && surface.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace && surface.localScale == Vector3.one * .001f &&
            surface.localPosition == surfacePosition && surface.localRotation == surfaceRotation &&
            Vector2.Distance(surface.rect.size, content.CanvasSize) < .01f && structure == Structure();

        public string StateSignature => IsPresentation ? PresentationSignature() : new JObject { ["content"] = content?.DeclaredJson(), ["intact"] = IsIntact(),
            ["structure"] = Structure(), ["actualText"] = body != null ? body.text : null,
            ["actualFontSize"] = body != null ? body.fontSize : 0, ["richText"] = body != null && body.richText,
            ["actualTitle"] = heading != null ? heading.text : null,
            ["textEnabled"] = body != null && body.enabled && body.gameObject.activeSelf,
            ["font"] = body != null && body.font != null ? body.font.GetInstanceID() : 0,
            ["width"] = surface != null ? surface.rect.width : 0, ["height"] = surface != null ? surface.rect.height : 0
        }.ToString(Newtonsoft.Json.Formatting.None);

        public void RequireIntact()
        { if (!IsIntact()) throw new InvalidOperationException("Panel visuals were modified externally; the operation cannot safely reconstruct them."); }

        public Vector3[] EnvelopeCorners()
        {
            RequireIntact();
            var corners = new Vector3[4]; surface.GetWorldCorners(corners);
            var result = corners.ToList();
            if (window != null)
                foreach (var border in window.GrabFrame.GetComponentsInChildren<BoxCollider>(true))
                    for (int i = 0; i < 8; i++) result.Add(border.transform.TransformPoint(border.center + Vector3.Scale(border.size * .5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result.ToArray();
        }
    }
}
