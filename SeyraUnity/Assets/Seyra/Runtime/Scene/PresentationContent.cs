using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Presentations;

namespace Seyra.SceneTools
{
    public sealed class PresentationContent
    {
        public double Width { get; private set; }
        public double Height { get; private set; }
        public UIPresentationData Data { get; private set; }
        public PanelContent Envelope => new PanelContent { kind = "panel", title = Data.title, text = Data.title,
            width = Width, height = Height, fontSize = 18 };
        public JObject ToJson() => new JObject { ["version"] = 1, ["width"] = Width, ["height"] = Height,
            ["presentation"] = UIPresentationCodec.Encode(Data) };
        public static PresentationContent Parse(JToken token)
        {
            var json = token as JObject ?? throw new ArgumentException("Presentation content must be an object.");
            RequireKeys(json, "version", "width", "height", "presentation");
            if (json["version"].Type != JTokenType.Integer || (int)json["version"] != 1) throw new ArgumentException("Unsupported presentation content version.");
            double Size(string key)
            {
                var t = json[key];
                if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new ArgumentException("Presentation dimensions must be numeric.");
                double n = (double)t;
                if (double.IsNaN(n) || double.IsInfinity(n) || n < .4 || n > 2) throw new ArgumentException("Presentation dimensions must be between 0.4 and 2 meters.");
                return n;
            }
            return new PresentationContent { Width = Size("width"), Height = Size("height"), Data = UIPresentationCodec.Parse(json["presentation"] as JObject) };
        }
        internal static void RequireKeys(JObject json, params string[] keys)
        {
            var actual = new HashSet<string>(json.Properties().Select(p => p.Name), StringComparer.Ordinal);
            if (!actual.SetEquals(keys)) throw new ArgumentException("Missing or unknown presentation/state fields.");
        }
    }
}
