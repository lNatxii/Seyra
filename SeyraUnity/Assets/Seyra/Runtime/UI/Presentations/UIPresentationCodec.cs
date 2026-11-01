using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Seyra.UI.Presentations
{
    /// <summary>Same strict boundary for MCP, WebSocket and typed C# callers. Validate before touching Unity.</summary>
    public static class UIPresentationCodec
    {
        public static readonly IReadOnlyDictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["mediaCard"] = typeof(MediaCardData),
            ["infoPanel"] = typeof(InfoPanelData), ["technicalSheet"] = typeof(TechnicalSheetData),
            ["gallery"] = typeof(GalleryData), ["stepGuide"] = typeof(StepGuideData),
            ["documentViewer"] = typeof(DocumentViewerData), ["comparison"] = typeof(ComparisonData),
            ["checklist"] = typeof(ChecklistData), ["diagnostic"] = typeof(DiagnosticData),
            ["choiceSelector"] = typeof(ChoiceSelectorData), ["parameterForm"] = typeof(ParameterFormData),
            ["dataView"] = typeof(DataViewData), ["workPlan"] = typeof(WorkPlanData)
        };
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, TypeNameHandling = TypeNameHandling.None };
        public static JObject Encode(UIPresentationData data) => JObject.FromObject(data ?? throw new ArgumentNullException(nameof(data)), JsonSerializer.Create(Settings));
        public static UIPresentationData CopyValidated(UIPresentationData data) => Parse(Encode(data));
        public static UIPresentationData Parse(JObject json)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json.ToString(Formatting.None)) > 131072) throw new ArgumentException("UI content is missing or exceeds 128 KiB.");
            var kind = json["type"]?.Type == JTokenType.String ? (string)json["type"] : null;
            if (kind == null || !Types.TryGetValue(kind, out var type)) throw new ArgumentException("Unknown presentation type.");
            ValidateObject(json, type, "presentation", true);
            var data = (UIPresentationData)json.ToObject(type, JsonSerializer.Create(Settings));
            if (data.id.Length > 128) throw new ArgumentException("id accepts at most 128 characters.");
            if (data.title.Length > 256) throw new ArgumentException("title accepts at most 256 characters.");
            ValidateRelations(data);
            return data;
        }
        static void ValidateObject(JObject json, Type type, string path, bool root = false)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var names = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);
            if (root) names.Add("type");
            if (json.Properties().Any(p => !names.Contains(p.Name))) throw new ArgumentException(path + ": unknown property.");
            foreach (var field in fields)
            {
                var token = json[field.Name]; var required = field.IsDefined(typeof(UIRequiredAttribute));
                if (token == null) { if (required) throw new ArgumentException(path + "." + field.Name + ": required."); continue; }
                if (type == typeof(UIPhotoData) && (field.Name == "source" || field.Name == "sourceUrl"))
                {
                    int maximum = field.Name == "source" ? 300 : 2048;
                    if (token.Type != JTokenType.String || ((string)token).Length > maximum)
                        throw new ArgumentException(path + "." + field.Name + ": text required (maximum " + maximum + " characters).");
                }
                ValidateValue(token, field.FieldType, path + "." + field.Name, field.GetCustomAttribute<UICountAttribute>(), required, field.GetCustomAttribute<UIChoicesAttribute>());
            }
        }
        static void ValidateValue(JToken token, Type type, string path, UICountAttribute count = null, bool required = false, UIChoicesAttribute choices = null)
        {
            if (type == typeof(string))
            {
                if (token.Type != JTokenType.String || ((string)token).Length > 4000 || (required && string.IsNullOrWhiteSpace((string)token)))
                    throw new ArgumentException(path + ": invalid text (maximum 4000 characters).");
                if (choices != null && !choices.Values.Contains((string)token)) throw new ArgumentException(path + ": unsupported choice.");
            }
            else if (type == typeof(bool)) { if (token.Type != JTokenType.Boolean) throw new ArgumentException(path + ": boolean required."); }
            else if (type == typeof(double))
            {
                if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new ArgumentException(path + ": number required.");
                var n = (double)token;
                if (double.IsInfinity(n) || double.IsNaN(n) || Math.Abs(n) > 1e12) throw new ArgumentException(path + ": number out of range.");
            }
            else if (type.IsArray)
            {
                var a = token as JArray;
                if (a == null || a.Count < (count?.Min ?? 0) || a.Count > (count?.Max ?? 100)) throw new ArgumentException(path + ": invalid length.");
                for (var i = 0; i < a.Count; i++) ValidateValue(a[i], type.GetElementType(), path + "[" + i + "]");
            }
            else ValidateObject(token as JObject ?? throw new ArgumentException(path + ": object required."), type, path);
        }
        static void Unique(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids) if (id.Length > 128 || !set.Add(id)) throw new ArgumentException("Item IDs are duplicated or too long.");
        }
        public static UICardCheckData CardCheck(UIPresentationData data) => data is InfoPanelData info ? info.check :
            data is TechnicalSheetData technical ? technical.check : data is MediaCardData media ? media.check : null;
        static void ValidateRelations(UIPresentationData data)
        {
            var check = CardCheck(data);
            if (check != null)
            {
                check.label = check.label.Trim();
                if (check.label.Length < 1 || check.label.Length > 24) throw new ArgumentException("check.label must contain 1 to 24 characters.");
            }
            switch (data)
            {
                case StepGuideData d: Unique(d.steps.Select(x => x.id)); break;
                case ComparisonData d:
                    Unique(d.alternatives.Select(x => x.id));
                    if (d.alternatives.Any(x => x.values.Length != d.criteria.Length)) throw new ArgumentException("Each alternative requires one value per criterion."); break;
                case ChecklistData d: Unique(d.items.Select(x => x.id)); break;
                case ChoiceSelectorData d: Unique(d.options.Select(x => x.id)); break;
                case DiagnosticData d:
                    Unique(d.nodes.Select(x => x.id));
                    var nodes = new HashSet<string>(d.nodes.Select(x => x.id));
                    if (!nodes.Contains(d.startNodeId)) throw new ArgumentException("Unknown initial node.");
                    foreach (var node in d.nodes)
                    {
                        Unique(node.answers.Select(x => x.id));
                        if (node.answers.Any(a => !nodes.Contains(a.nextNodeId))) throw new ArgumentException("Answer destination is unknown.");
                    } break;
                case ParameterFormData d:
                    Unique(d.fields.Select(x => x.id));
                    foreach (var f in d.fields)
                    {
                        if (f.min > f.max) throw new ArgumentException("Field limits are inverted.");
                        if (f.type == "choice" && (f.choices.Length == 0 || f.choices.Distinct().Count() != f.choices.Length || f.choices.Any(string.IsNullOrWhiteSpace))) throw new ArgumentException("Field choices are empty or duplicated.");
                        if (f.value.Length > 0 && !TryFieldValue(f, f.value, out _)) throw new ArgumentException("Invalid initial field value: " + f.id);
                    } break;
                case DataViewData d:
                    if (d.mode == "table" && (d.columns.Length == 0 || d.rows.Length == 0 || d.bars.Length != 0 || d.rows.Any(r => r.cells.Length != d.columns.Length))) throw new ArgumentException("Table requires complete columns and rows, without bars.");
                    if (d.mode == "bars" && (d.bars.Length == 0 || d.columns.Length != 0 || d.rows.Length != 0)) throw new ArgumentException("Bar chart requires bars, without table data."); break;
                case WorkPlanData d:
                    Unique(d.tasks.Select(x => x.id));
                    var tasks = d.tasks.ToDictionary(t => t.id); var visited = new HashSet<string>(); var visiting = new HashSet<string>();
                    foreach (var task in d.tasks) Visit(task.id, tasks, visited, visiting); break;
            }
        }
        static void Visit(string id, Dictionary<string, UIWorkTaskData> tasks, HashSet<string> visited, HashSet<string> visiting)
        {
            if (visited.Contains(id)) return;
            if (!tasks.ContainsKey(id) || !visiting.Add(id)) throw new ArgumentException("Task dependency is unknown or cyclic.");
            foreach (var dependency in tasks[id].dependsOn) Visit(dependency, tasks, visited, visiting);
            visiting.Remove(id); visited.Add(id);
        }
        public static bool TryFieldValue(UIFormFieldData field, string text, out JToken value)
        {
            value = JValue.CreateNull();
            if (string.IsNullOrWhiteSpace(text)) return !field.required;
            switch (field.type)
            {
                case "number":
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsNaN(n) || double.IsInfinity(n) || n < field.min || n > field.max) return false;
                    value = new JValue(n); return true;
                case "toggle": if (text != "true" && text != "false") return false; value = new JValue(text == "true"); return true;
                case "choice": if (!field.choices.Contains(text)) return false; value = new JValue(text); return true;
                default: if (text.Length > 4000) return false; value = new JValue(text); return true;
            }
        }
    }
}
