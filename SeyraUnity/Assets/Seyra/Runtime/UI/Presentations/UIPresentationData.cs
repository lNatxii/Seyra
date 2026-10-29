using System;
using Newtonsoft.Json;

namespace Seyra.UI.Presentations
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class UIRequiredAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class UICountAttribute : Attribute
    { public readonly int Min, Max; public UICountAttribute(int min, int max) { Min = min; Max = max; } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class UIChoicesAttribute : Attribute
    { public readonly string[] Values; public UIChoicesAttribute(params string[] values) { Values = values; } }

    // Serializable content contracts: no transforms, arbitrary callbacks, URLs or executable code.
    public abstract class UIPresentationData
    {
        [UIRequired] public string id, title;
        public string description = "";
        [JsonProperty("type")] public abstract string Type { get; }
    }
    public sealed class UIPhotoData
    {
        [UIRequired] public string imageRef, title;
        public string description = "", alt = "";
    }
    public sealed class UIPropertyData
    { [UIRequired] public string label, value; public string unit = ""; }
    public sealed class UISectionData
    { [UIRequired] public string title, description; }
    public sealed class InfoPanelData : UIPresentationData
    {
        public override string Type => "infoPanel";
        public UIPhotoData image;
        [UICount(0, 20)] public string[] highlights = Array.Empty<string>();
    }
    public sealed class MediaCardData : UIPresentationData
    {
        public override string Type => "mediaCard";
        [UIRequired] public UIPhotoData image;
        [UIRequired] public string body;
        [UICount(0, 8)] public UIPropertyData[] facts = Array.Empty<UIPropertyData>();
        [UICount(0, 6)] public string[] tags = Array.Empty<string>();
    }
    public sealed class TechnicalSheetData : UIPresentationData
    {
        public override string Type => "technicalSheet";
        [UIRequired, UICount(1, 50)] public UIPropertyData[] properties;
        [UICount(0, 20)] public UISectionData[] sections = Array.Empty<UISectionData>();
        [UICount(0, 20)] public UIPhotoData[] images = Array.Empty<UIPhotoData>();
    }
    public sealed class GalleryData : UIPresentationData
    { public override string Type => "gallery"; [UIRequired, UICount(1, 50)] public UIPhotoData[] images; }
    public sealed class UIStepData
    {
        [UIRequired] public string id, title, description;
        public string notice = "";
        [UICount(0, 10)] public UIPhotoData[] images = Array.Empty<UIPhotoData>();
    }
    public sealed class StepGuideData : UIPresentationData
    {
        public override string Type => "stepGuide";
        [UICount(0, 30)] public string[] prerequisites = Array.Empty<string>();
        [UIRequired, UICount(1, 50)] public UIStepData[] steps;
    }
    public sealed class UIDocumentData
    {
        [UIRequired] public string imageRef, title;
        public string description = "", alt = "", legend = "";
        [UICount(0, 30)] public string[] annotations = Array.Empty<string>();
    }
    public sealed class DocumentViewerData : UIPresentationData
    { public override string Type => "documentViewer"; [UIRequired, UICount(1, 30)] public UIDocumentData[] documents; }
    public sealed class UIAlternativeData
    {
        [UIRequired] public string id, title;
        public string description = "";
        [UIRequired, UICount(1, 20)] public string[] values;
        [UICount(0, 10)] public string[] advantages = Array.Empty<string>(), disadvantages = Array.Empty<string>();
    }
    public sealed class ComparisonData : UIPresentationData
    {
        public override string Type => "comparison";
        [UIRequired, UICount(1, 20)] public string[] criteria;
        [UIRequired, UICount(2, 8)] public UIAlternativeData[] alternatives;
        public string recommendation = "";
    }
    public sealed class UICheckItemData
    { [UIRequired] public string id, title; public string description = "", group = ""; public bool completed; }
    public sealed class ChecklistData : UIPresentationData
    { public override string Type => "checklist"; [UIRequired, UICount(1, 100)] public UICheckItemData[] items; }
    public sealed class UIDiagnosticAnswerData
    { [UIRequired] public string id, label, nextNodeId; }
    public sealed class UIDiagnosticNodeData
    {
        [UIRequired] public string id, title, description;
        [UICount(0, 8)] public UIDiagnosticAnswerData[] answers = Array.Empty<UIDiagnosticAnswerData>();
    }
    public sealed class DiagnosticData : UIPresentationData
    {
        public override string Type => "diagnostic";
        [UIRequired] public string startNodeId;
        [UIRequired, UICount(1, 50)] public UIDiagnosticNodeData[] nodes;
    }
    public sealed class UIChoiceData
    { [UIRequired] public string id, title; public string description = ""; public UIPhotoData image; }
    public sealed class ChoiceSelectorData : UIPresentationData
    {
        public override string Type => "choiceSelector";
        public bool multiple;
        [UIRequired, UICount(1, 30)] public UIChoiceData[] options;
        public string submitLabel = "Elegir";
    }
    public sealed class UIFormFieldData
    {
        [UIRequired] public string id, label;
        [UIRequired, UIChoices("text", "number", "toggle", "choice")] public string type;
        public string help = "", unit = "", value = "";
        public bool required;
        public double min = -1000000000000, max = 1000000000000;
        [UICount(0, 50)] public string[] choices = Array.Empty<string>();
    }
    public sealed class ParameterFormData : UIPresentationData
    {
        public override string Type => "parameterForm";
        [UIRequired, UICount(1, 30)] public UIFormFieldData[] fields;
        public string submitLabel = "Enviar";
    }
    public sealed class UITableRowData { [UIRequired, UICount(1, 12)] public string[] cells; }
    public sealed class UIBarData
    { [UIRequired] public string label; [UIRequired] public double value; public string unit = ""; }
    public sealed class DataViewData : UIPresentationData
    {
        public override string Type => "dataView";
        [UIRequired, UIChoices("table", "bars")] public string mode;
        [UICount(0, 12)] public string[] columns = Array.Empty<string>();
        [UICount(0, 100)] public UITableRowData[] rows = Array.Empty<UITableRowData>();
        [UICount(0, 50)] public UIBarData[] bars = Array.Empty<UIBarData>();
    }
    public sealed class UIWorkTaskData
    {
        [UIRequired] public string id, title;
        public string description = "", phase = "";
        [UIChoices("pending", "active", "done", "blocked")] public string status = "pending";
        [UICount(0, 30)] public string[] dependsOn = Array.Empty<string>();
    }
    public sealed class WorkPlanData : UIPresentationData
    { public override string Type => "workPlan"; [UIRequired, UICount(1, 100)] public UIWorkTaskData[] tasks; }
}
