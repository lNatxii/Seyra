using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;

namespace Seyra.UI.Presentations
{
    /// <summary>Validates persisted interaction against its exact content, never against arbitrary controls.</summary>
    public static class UIPresentationState
    {
        public static JObject Validate(UIPresentationData data, JObject state)
        {
            if (state == null) throw new ArgumentException("Presentation interaction state is required.");
            PresentationContent.RequireKeys(state, "version", "revision", "index", "nodeId", "values", "submitted", "submission", "lastAction", "scroll", "zoom", "visible");
            int Integer(string key, int maximum)
            {
                if (state[key].Type != JTokenType.Integer || (long)state[key] < 0 || (long)state[key] > maximum) throw new ArgumentException("Invalid presentation state: " + key);
                return (int)state[key];
            }
            if (Integer("version", 1) != 1) throw new ArgumentException("Unsupported interaction state version.");
            Integer("revision", int.MaxValue);
            if (state["submitted"].Type != JTokenType.Boolean || state["visible"].Type != JTokenType.Boolean || state["lastAction"].Type != JTokenType.String || ((string)state["lastAction"]).Length > 128)
                throw new ArgumentException("Invalid presentation flags/action.");
            var values = state["values"] as JObject ?? throw new ArgumentException("Interaction values must be an object.");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            int pages = 1;
            if (data is GalleryData gallery) pages = gallery.images.Length;
            if (data is DocumentViewerData documents) pages = documents.documents.Length;
            if (data is StepGuideData guide) { pages = guide.steps.Length; expected.UnionWith(guide.steps.Select(s => s.id)); }
            if (data is ChecklistData checklist) expected.UnionWith(checklist.items.Select(i => i.id));
            if (data is ChoiceSelectorData choice) expected.UnionWith(choice.options.Select(o => o.id));
            if (data is ParameterFormData form) expected.UnionWith(form.fields.Select(f => f.id));
            if (data is DiagnosticData diagnostic)
            {
                pages = diagnostic.nodes.Length;
                if (state["nodeId"].Type != JTokenType.String || !diagnostic.nodes.Any(n => n.id == (string)state["nodeId"])) throw new ArgumentException("Unknown restored diagnostic node.");
                if (Integer("index", pages - 1) != Array.FindIndex(diagnostic.nodes, n => n.id == (string)state["nodeId"])) throw new ArgumentException("Diagnostic node/index mismatch.");
                foreach (var property in values.Properties())
                    if (property.Value.Type != JTokenType.String || !diagnostic.nodes.Any(n => n.id == property.Name && n.answers.Any(a => a.id == (string)property.Value)))
                        throw new ArgumentException("Unknown restored diagnostic answer.");
            }
            else
            {
                Integer("index", pages - 1);
                if (state["nodeId"].Type != JTokenType.Null) throw new ArgumentException("Only diagnostics have nodeId state.");
                if (!expected.SetEquals(values.Properties().Select(p => p.Name))) throw new ArgumentException("Restored values do not match the content IDs.");
            }
            foreach (var property in values.Properties())
            {
                if (property.Value.Type != JTokenType.String || ((string)property.Value).Length > 4000) throw new ArgumentException("Invalid restored control value.");
                if (data is StepGuideData || data is ChecklistData || data is ChoiceSelectorData)
                    if ((string)property.Value != "true" && (string)property.Value != "false") throw new ArgumentException("Restored toggle must be true or false.");
            }
            if (data is ChoiceSelectorData selector && !selector.multiple && values.Properties().Count(p => (string)p.Value == "true") > 1) throw new ArgumentException("Multiple selections in a single-choice selector.");
            if (data is ParameterFormData draftForm)
                foreach (var field in draftForm.fields)
                {
                    var value = (string)values[field.id];
                    if (field.type == "toggle" && value != "true" && value != "false") throw new ArgumentException("Invalid restored form toggle.");
                    if (field.type == "choice" && !field.choices.Contains(value)) throw new ArgumentException("Unknown restored form choice.");
                }
            var scroll = state["scroll"] as JObject ?? throw new ArgumentException("Missing restored scroll position.");
            PresentationContent.RequireKeys(scroll, "x", "y");
            foreach (var property in scroll.Properties())
                if ((property.Value.Type != JTokenType.Float && property.Value.Type != JTokenType.Integer) || !double.IsFinite((double)property.Value) || (double)property.Value < 0 || (double)property.Value > 1)
                    throw new ArgumentException("Invalid restored scroll position.");
            var zoom = state["zoom"] as JObject ?? throw new ArgumentException("Missing image zoom state.");
            var photos = Photos(data).ToArray();
            var zoomKeys = new HashSet<string>(photos.Select((p, i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), StringComparer.Ordinal);
            // Zoom is optional per registered image: unavailable media has no zoom control.
            foreach (var property in zoom.Properties())
                if (!zoomKeys.Contains(property.Name) || property.Value.Type != JTokenType.Integer || ((int)property.Value != 1 && (int)property.Value != 2))
                    throw new ArgumentException("Invalid restored image zoom.");
            if ((bool)state["submitted"])
            {
                JObject expectedSubmission;
                if (data is ParameterFormData submittedForm)
                {
                    expectedSubmission = new JObject();
                    foreach (var field in submittedForm.fields)
                    {
                        if (!UIPresentationCodec.TryFieldValue(field, (string)values[field.id], out var value)) throw new ArgumentException("Submitted form contains invalid values.");
                        expectedSubmission[field.id] = value;
                    }
                }
                else if (data is ChoiceSelectorData)
                {
                    var ids = values.Properties().Where(p => (string)p.Value == "true").Select(p => p.Name).ToArray();
                    if (ids.Length == 0) throw new ArgumentException("Submitted selector has no selection.");
                    expectedSubmission = new JObject { ["selectedIds"] = new JArray(ids) };
                }
                else throw new ArgumentException("This presentation does not submit data.");
                // JSON number spelling is irrelevant; compare a canonical parsed representation.
                if (state["submission"] is not JObject submission || !Equivalent(expectedSubmission, submission)) throw new ArgumentException("Submission does not match restored values.");
            }
            else if (state["submission"].Type != JTokenType.Null) throw new ArgumentException("Unsubmitted state cannot contain a submission.");
            return (JObject)state.DeepClone();
        }
        static bool Equivalent(JToken a, JToken b)
        {
            if ((a.Type == JTokenType.Float || a.Type == JTokenType.Integer) && (b.Type == JTokenType.Float || b.Type == JTokenType.Integer)) return (double)a == (double)b;
            if (a is JObject ao && b is JObject bo) return ao.Count == bo.Count && ao.Properties().All(p => bo.TryGetValue(p.Name, out var value) && Equivalent(p.Value, value));
            if (a is JArray aa && b is JArray ba) return aa.Count == ba.Count && aa.Zip(ba, Equivalent).All(v => v);
            return JToken.DeepEquals(a, b);
        }
        public static IEnumerable<UIPhotoData> Photos(UIPresentationData data)
        {
            if (data is InfoPanelData info && info.image != null) yield return info.image;
            if (data is TechnicalSheetData technical) foreach (var p in technical.images) yield return p;
            if (data is GalleryData gallery) foreach (var p in gallery.images) yield return p;
            if (data is StepGuideData guide) foreach (var step in guide.steps) foreach (var p in step.images) yield return p;
            if (data is DocumentViewerData documents) foreach (var d in documents.documents) yield return new UIPhotoData { imageRef = d.imageRef };
            if (data is ChoiceSelectorData choices) foreach (var c in choices.options) if (c.image != null) yield return c.image;
        }
    }
}
