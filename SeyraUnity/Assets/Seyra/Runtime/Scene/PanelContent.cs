using System;
using Newtonsoft.Json.Linq;

namespace Seyra.SceneTools
{
    /// <summary>Versioned semantic content. Dimensions are meters; font size is millimeter Canvas units.</summary>
    [Serializable]
    public sealed class PanelContent
    {
        public string kind, title, text;
        public string checkLabel = "";
        public bool checkCompleted;
        public bool chip;
        public bool HasCheck => !string.IsNullOrEmpty(checkLabel);
        public double width, height, fontSize;
        internal UnityEngine.Vector2 CanvasSize => new UnityEngine.Vector2((float)width, (float)height) * 1000;
        public JObject ToJson()
        {
            var json = new JObject { ["version"] = 1, ["kind"] = kind, ["title"] = title,
            ["text"] = text, ["width"] = width, ["height"] = height, ["fontSize"] = fontSize };
            if (HasCheck) json["check"] = new JObject { ["label"] = checkLabel, ["completed"] = checkCompleted };
            if (chip) json["chip"] = true;
            return json;
        }
        public bool SameDeclared(PanelContent other) => other != null && JToken.DeepEquals(DeclaredJson(), other.DeclaredJson());
        internal JObject DeclaredJson()
        {
            var json = ToJson();
            if (HasCheck) json["check"]["completed"] = false;
            return json;
        }
        public PanelContent Copy() => Parse(ToJson());
        public static PanelContent Parse(JToken token)
        {
            var json = token as JObject ?? throw new ArgumentException("panel must be an object.");
            var keys = new System.Collections.Generic.HashSet<string> { "version", "kind", "title", "text", "width", "height", "fontSize" };
            foreach (var property in json.Properties()) if (property.Name != "check" && property.Name != "chip" && !keys.Remove(property.Name)) throw new ArgumentException("Unknown panel field.");
            if (keys.Count != 0 || json["version"].Type != JTokenType.Integer || (int)json["version"] != 1)
                throw new ArgumentException("Missing fields or unsupported panel content version.");
            string ReadText(string name, int limit)
            {
                if (json[name].Type != JTokenType.String || ((string)json[name]).Length > limit)
                    throw new ArgumentException(name + " exceeds its text limit or is not a string.");
                return (string)json[name];
            }
            double Number(string name, double minimum, double maximum)
            {
                var value = json[name];
                if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer) throw new ArgumentException(name + " must be numeric.");
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number) || number < minimum || number > maximum)
                    throw new ArgumentException(name + " is outside the panel limits.");
                return number;
            }
            var kind = ReadText("kind", 16);
            if (kind != "panel" && kind != "label") throw new ArgumentException("Panel kind must be panel or label.");
            var result = new PanelContent { kind = kind, title = ReadText("title", 256), text = ReadText("text", 16000),
                width = Number("width", .16, 2), height = Number("height", kind == "panel" ? .24 : .04, 2), fontSize = Number("fontSize", 12, 72) };
            if (string.IsNullOrWhiteSpace(result.text)) throw new ArgumentException("Panel text must not be empty.");
            if (kind == "label" && result.title.Length != 0) throw new ArgumentException("Labels require an empty title.");
            if (kind == "label" && result.height * 1000 < result.fontSize) throw new ArgumentException("Label height must fit at least one text line.");
            if (json.Property("chip") != null)
            {
                if (kind != "label") throw new ArgumentException("Only labels can be chips.");
                if (json["chip"].Type != JTokenType.Boolean || !(bool)json["chip"])
                    throw new ArgumentException("chip must be true when provided.");
                result.chip = true;
            }
            if (json.Property("check") != null)
            {
                if (kind == "label") throw new ArgumentException("Labels cannot have a check.");
                var check = json["check"] as JObject ?? throw new ArgumentException("check must be an object.");
                foreach (var field in check.Properties())
                    if (field.Name != "label" && field.Name != "completed") throw new ArgumentException("Unknown check field.");
                if (check["label"]?.Type != JTokenType.String) throw new ArgumentException("check.label is required.");
                result.checkLabel = ((string)check["label"]).Trim();
                if (result.checkLabel.Length < 1 || result.checkLabel.Length > 24) throw new ArgumentException("check.label must contain 1 to 24 characters.");
                if (check.Property("completed") != null)
                {
                    if (check["completed"].Type != JTokenType.Boolean) throw new ArgumentException("check.completed must be boolean.");
                    result.checkCompleted = (bool)check["completed"];
                }
            }
            return result;
        }
    }
}
