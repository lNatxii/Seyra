using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Seyra.SceneTools
{
    [Serializable]
    public sealed class InteractionSpec
    {
        public bool Grabbable, ReturnOnRelease = true, Exclusive = true, DetailOpen;
        public string DetailSide = "above", DetailId;
        public float DetailGap = .05f;
        public PanelContent DetailPanel;
        public PresentationContent DetailPresentation;
        public string Signature => new JObject { ["grabbable"] = Grabbable, ["returnOnRelease"] = ReturnOnRelease,
            ["exclusive"] = Exclusive, ["detailId"] = DetailId, ["detailSide"] = DetailSide, ["detailGap"] = DetailGap }.ToString(Formatting.None);
        public InteractionSpec Copy()
        {
            var copy = (InteractionSpec)MemberwiseClone();
            copy.DetailPanel = DetailPanel?.Copy();
            copy.DetailPresentation = DetailPresentation == null ? null : PresentationContent.Parse(DetailPresentation.ToJson());
            return copy;
        }
        public JObject ToJson()
        {
            var json = new JObject { ["grabbable"] = Grabbable, ["returnOnRelease"] = ReturnOnRelease,
                ["exclusive"] = Exclusive, ["detailSide"] = DetailSide, ["detailGap"] = DetailGap, ["detailOpen"] = DetailOpen };
            if (DetailPanel != null) json["detail"] = new JObject { ["panel"] = DetailPanel.ToJson() };
            else if (DetailPresentation != null) json["detail"] = new JObject { ["presentation"] = DetailPresentation.ToJson()["presentation"],
                ["width"] = DetailPresentation.Width, ["height"] = DetailPresentation.Height };
            return json;
        }
        public static InteractionSpec Parse(JToken token)
        {
            var json = token as JObject ?? throw new ArgumentException("Expected interaction fields.");
            var allowed = new[] { "grabbable", "returnOnRelease", "exclusive", "detail", "detailSide", "detailGap", "detailOpen" };
            if (json.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Unknown interaction field.");
            bool Flag(string name, bool fallback)
            {
                if (json.Property(name) == null) return fallback;
                if (json[name].Type != JTokenType.Boolean) throw new ArgumentException(name + " must be boolean.");
                return (bool)json[name];
            }
            var result = new InteractionSpec { Grabbable = Flag("grabbable", false), ReturnOnRelease = Flag("returnOnRelease", true),
                Exclusive = Flag("exclusive", true), DetailOpen = Flag("detailOpen", false) };
            if (json.Property("detailSide") != null)
            {
                if (json["detailSide"].Type != JTokenType.String || !new[] { "above", "right", "left" }.Contains((string)json["detailSide"]))
                    throw new ArgumentException("Invalid detailSide.");
                result.DetailSide = (string)json["detailSide"];
            }
            if (json.Property("detailGap") != null) result.DetailGap = MotionSpec.Number(json["detailGap"], 0, .5);
            if (json.Property("detail") != null)
            {
                var detail = json["detail"] as JObject ?? throw new ArgumentException("Expected a detail object.");
                if (detail.Property("panel") != null)
                {
                    PresentationContent.RequireKeys(detail, "panel");
                    result.DetailPanel = PanelContent.Parse(detail["panel"]);
                    if (result.DetailPanel.kind != "panel") throw new ArgumentException("Interaction details cannot be labels.");
                }
                else
                {
                    PresentationContent.RequireKeys(detail, "presentation", "width", "height");
                    var content = (JObject)detail.DeepClone(); content["version"] = 1;
                    result.DetailPresentation = PresentationContent.Parse(content);
                }
            }
            return result;
        }
    }
}
