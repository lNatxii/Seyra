using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Seyra.UI.Presentations
{
    /// <summary>Composes the visible content from presentation data, without navigation or media references.</summary>
    public static class UIPresentationSpeech
    {
        public static string Compose(UIPresentationView view)
        {
            var lines = new List<string>();
            void Add(string text) { if (!string.IsNullOrWhiteSpace(text)) lines.Add(text); }
            void Section(string title, string description) { Add(title); Add(description); }
            var data = view.Data;
            Section(data.title, data.description);
            switch (data)
            {
                case MediaCardData d:
                    Add(d.body);
                    foreach (var fact in d.facts) Add(fact.label + ": " + fact.value);
                    break;
                case InfoPanelData d:
                    foreach (var highlight in d.highlights) Add(highlight);
                    break;
                case TechnicalSheetData d:
                    foreach (var property in d.properties) Add(property.label + ": " + property.value + Unit(property.unit));
                    foreach (var section in d.sections) Section(section.title, section.description);
                    break;
                case GalleryData d:
                    var image = d.images[view.Index];
                    Add($"Image {view.Index + 1} of {d.images.Length}");
                    Section(image.title, string.IsNullOrWhiteSpace(image.description) ? image.alt : image.description);
                    break;
                case StepGuideData d:
                    if (view.Index == 0 && d.prerequisites.Length > 0)
                    {
                        Add("Before you start:");
                        foreach (var prerequisite in d.prerequisites) Add(prerequisite);
                    }
                    var step = d.steps[view.Index];
                    Add($"Step {view.Index + 1} of {d.steps.Length}: {step.title}");
                    Section(step.description, step.notice);
                    break;
                case DocumentViewerData d:
                    var document = d.documents[view.Index];
                    Add($"Document {view.Index + 1} of {d.documents.Length}");
                    Section(document.title, document.description); Add(document.legend);
                    foreach (var annotation in document.annotations) Add(annotation);
                    break;
                case ComparisonData d:
                    if (!string.IsNullOrWhiteSpace(d.recommendation)) Add("Recommendation: " + d.recommendation);
                    foreach (var alternative in d.alternatives)
                    {
                        Section(alternative.title, alternative.description);
                        for (var i = 0; i < d.criteria.Length; i++) Add(d.criteria[i] + ": " + alternative.values[i]);
                        foreach (var pro in alternative.advantages) Add("Pro: " + pro);
                        foreach (var con in alternative.disadvantages) Add("Con: " + con);
                    }
                    break;
                case ChecklistData d:
                    var remaining = d.items.Where(item => !view.Values.TryGetValue(item.id, out var value) || value != "true").ToArray();
                    Add($"{d.items.Length - remaining.Length} of {d.items.Length} completed");
                    Add(remaining.Length == 0 ? "All checks are completed." : "Still to do:");
                    foreach (var item in remaining) Section(item.title, item.description);
                    break;
                case DiagnosticData d:
                    var node = d.nodes.First(n => n.id == view.NodeId);
                    Section(node.title, node.description);
                    if (node.answers.Length > 0) Add("Options: " + string.Join(", ", node.answers.Select(answer => answer.label)) + ".");
                    break;
                case ChoiceSelectorData d:
                    foreach (var option in d.options) Section(option.title, option.description);
                    break;
                case ParameterFormData d:
                    foreach (var field in d.fields)
                    {
                        Section(field.label + (string.IsNullOrEmpty(field.unit) ? "" : " (" + field.unit + ")"), field.help);
                        if (view.Values.TryGetValue(field.id, out var value) && !string.IsNullOrWhiteSpace(value)) Add("Current value: " + value);
                    }
                    break;
                case DataViewData d:
                    if (d.mode == "table")
                        foreach (var row in d.rows) Add(string.Join(", ", d.columns.Select((column, i) => column + ": " + row.cells[i])));
                    else
                        foreach (var bar in d.bars) Add(bar.label + ": " + bar.value.ToString("0.###", CultureInfo.InvariantCulture) + Unit(bar.unit));
                    break;
                case WorkPlanData d:
                    Add($"{d.tasks.Count(task => task.status == "done")} of {d.tasks.Length} tasks completed");
                    foreach (var task in d.tasks.Where(task => task.status != "done"))
                        Section(task.title + ": " + (task.status == "active" ? "In progress" : task.status == "blocked" ? "Blocked" : "Pending"), task.description);
                    break;
            }
            return string.Join("\n", lines);
        }

        static string Unit(string unit) => string.IsNullOrWhiteSpace(unit) ? "" : " " + unit;
    }
}
