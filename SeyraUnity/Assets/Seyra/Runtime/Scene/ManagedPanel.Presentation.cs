using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.SceneTools
{
    public sealed partial class ManagedPanel
    {
        [SerializeField] string presentationJson;
        [SerializeField] UIPresentationView presentationView;
        [SerializeField] UIPresentationCatalog templates;
        [SerializeField] bool presentationVisible = true;
        readonly HashSet<Transform> retiredPresentationSlots = new HashSet<Transform>();
        readonly Dictionary<TMP_Text, string> expectedPresentationText = new Dictionary<TMP_Text, string>();
        PresentationContent preparingPresentation;
        internal IEnumerable<string> MediaReferences()
        {
            var contents = new List<JObject>();
            if (IsPresentation) contents.Add(JObject.Parse(presentationJson));
            if (preparingPresentation != null) contents.Add(preparingPresentation.ToJson());
            return contents.SelectMany(json => json.Descendants().OfType<JProperty>())
                .Where(property => property.Name == "imageRef" && property.Value.Type == JTokenType.String)
                .Select(property => (string)property.Value).ToArray();
        }
        public bool IsPresentation => !string.IsNullOrEmpty(presentationJson);
        public UIPresentationView PresentationView => presentationView;
        public PresentationContent Presentation => PresentationContent.Parse(JObject.Parse(presentationJson));
        public JObject InteractionState => presentationView != null ? presentationView.ExportState(presentationVisible) : null;
        public void InitializePresentation(PresentationContent spec, UIPrefabCatalog library, UIPresentationCatalog catalog, JObject state = null)
        {
            if (content != null) throw new InvalidOperationException("The managed view is already initialized.");
            if (catalog == null || catalog.Components != library) throw new InvalidOperationException("Missing or incompatible presentation catalog.");
            templates = catalog;
            window = UIXRWindow.Create(library, transform, spec.Envelope.CanvasSize);
            window.name = "PresentationWindow";
            window.GrabFrame.Grabbable.InjectOptionalTargetTransform(transform);
            surface = (RectTransform)window.Canvas.transform;
            ApplyPresentation(spec, state);
        }
        public void ApplyPresentation(PresentationContent spec, JObject state = null)
        {
            spec = PresentationContent.Parse(spec.ToJson());
            if (state != null) state = UIPresentationState.Validate(spec.Data, state);
            var stage = new GameObject("PresentationSlot", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            stage.SetActive(false); stage.transform.SetParent(window.Content, false);
            var layout = stage.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            var size = stage.GetComponent<LayoutElement>(); size.minHeight = 0; size.preferredHeight = 0; size.flexibleHeight = size.flexibleWidth = 1;
            UIPresentationView next;
            try
            {
                preparingPresentation = spec;
                next = Instantiate(templates.Get(spec.Data.Type), stage.transform, false);
                var resolver = new PresentationMediaResolver(GetComponentInParent<RemoteMediaStore>(true), templates);
                next.FillValidated(spec.Data, templates.Components, resolver, () => SetPresentationVisible(false));
                if (state != null) next.RestoreState(state);
                next.ExportState(state != null ? (bool)state["visible"] : presentationVisible);
                // Include old + prepared trees in the physical retained-object budget.
                var root = transform; while (root.parent != null && root.GetComponent<GeneratedObject>()?.Kind != "root") root = root.parent;
                if (root.GetComponentsInChildren<Transform>(true).Length > 8192) throw new InvalidOperationException("Retained object limit reached while preparing the presentation.");
            }
            catch { DestroyImmediate(stage); throw; }
            finally { preparingPresentation = null; }
            var previous = presentationView;
            if (previous != null)
            {
                previous.ActionRequested -= PresentationAction;
                var old = previous.transform.parent; retiredPresentationSlots.Add(old); old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }
            content = spec.Envelope; presentationJson = spec.ToJson().ToString(Newtonsoft.Json.Formatting.None); presentationView = next;
            window.Resize(content.CanvasSize);
            next.ResizeMediaHeader();
            stage.SetActive(true);
            next.ActionRequested += PresentationAction;
            window.SetTheme(window.Themes.CurrentThemeIndex);
            UIAssetResources.EnsureTextMaterials(next.gameObject);
            presentationVisible = state != null ? (bool)state["visible"] : presentationVisible;
            window.gameObject.SetActive(presentationVisible);
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(surface);
            if (state != null) next.RestoreState(state);
            CapturePresentationIntegrity();
        }
        public void SetPresentationVisible(bool visible)
        {
            presentationVisible = visible; window.gameObject.SetActive(visible);
        }
        void PresentationAction(UIPresentationView view, string action) => CapturePresentationIntegrity();
        bool IsRetiredPresentationTransform(Transform t) => retiredPresentationSlots.Any(slot => slot != null && (t == slot || t.IsChildOf(slot)));
        void CapturePresentationIntegrity()
        {
            expectedPresentationText.Clear();
            foreach (var text in presentationView.GetComponentsInChildren<TMP_Text>(true))
                if (text.GetComponentInParent<TMP_InputField>(true) == null) expectedPresentationText[text] = text.text;
            foreach (var text in presentationView.GetComponentsInChildren<TMP_Text>(true)) text.richText = false;
            structure = Structure(); surfacePosition = surface.localPosition; surfaceRotation = surface.localRotation;
        }
        bool PresentationIsIntact() => window != null && presentationView != null && surface != null &&
            window.transform.localPosition == Vector3.zero && window.transform.localRotation == Quaternion.identity && window.transform.localScale == Vector3.one &&
            surface.localScale == Vector3.one * .001f && surface.localPosition == surfacePosition && surface.localRotation == surfaceRotation &&
            Vector2.Distance(surface.rect.size, content.CanvasSize) < .01f && Structure() == structure &&
            expectedPresentationText.All(pair => pair.Key != null && pair.Key.text == pair.Value && !pair.Key.richText);
        string PresentationSignature() => new JObject { ["presentation"] = JObject.Parse(presentationJson), ["intact"] = PresentationIsIntact() }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
