using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    [Serializable]
    public sealed class LineContent
    {
        public sealed class Endpoint
        {
            public string EntityId;
            public Vector3 Position;
            public JObject ToJson() => EntityId != null ? new JObject { ["entityId"] = EntityId } : new JObject { ["position"] = JsonVector(Position) };
        }
        public Endpoint[] Points;
        public Color Color = Color.white;
        public float Width = .01f;
        static JObject JsonVector(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        internal static void Fields(JObject value, params string[] allowed)
        {
            if (value.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Unknown line field.");
        }
        internal static float Number(JToken token, double minimum, double maximum)
        {
            if (token == null || token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new ArgumentException("Expected a finite number.");
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || (float)value < (float)minimum || (float)value > (float)maximum) throw new ArgumentException("Number is outside the allowed range.");
            return (float)value;
        }
        internal static Vector3 ReadVector(JToken token)
        {
            var value = token as JObject ?? throw new ArgumentException("Expected a vector.");
            Fields(value, "x", "y", "z");
            return new Vector3(Number(value["x"], -float.MaxValue, float.MaxValue), Number(value["y"], -float.MaxValue, float.MaxValue), Number(value["z"], -float.MaxValue, float.MaxValue));
        }
        public JObject ToJson() => new JObject { ["points"] = new JArray(Points.Select(p => p.ToJson())), ["width"] = Width,
            ["color"] = new JObject { ["r"] = Color.r, ["g"] = Color.g, ["b"] = Color.b, ["a"] = 1 } };
        public LineContent Copy() => Parse(ToJson());
        public static LineContent Parse(JToken token)
        {
            var value = token as JObject ?? throw new ArgumentException("line must be an object.");
            Fields(value, "points", "color", "width");
            var points = value["points"] as JArray ?? throw new ArgumentException("points must be an array.");
            if (points.Count < 2 || points.Count > 16) throw new ArgumentException("Expected 2..16 line points.");
            var result = new LineContent { Points = points.Select(p => {
                var endpoint = p as JObject ?? throw new ArgumentException("Expected an endpoint object.");
                Fields(endpoint, "entityId", "position");
                if (endpoint.Count != 1) throw new ArgumentException("An endpoint requires exactly one field.");
                if (endpoint["position"] != null) return new Endpoint { Position = ReadVector(endpoint["position"]) };
                var id = endpoint["entityId"];
                if (id?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)id) || ((string)id).Length > 128 || string.Equals(((string)id).Trim(), "null", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid endpoint ID.");
                return new Endpoint { EntityId = ((string)id).Trim() };
            }).ToArray() };
            if (value["width"] != null) result.Width = Number(value["width"], .001, .05);
            if (value["color"] != null)
            {
                var color = value["color"] as JObject ?? throw new ArgumentException("Expected a color.");
                Fields(color, "r", "g", "b", "a");
                result.Color = new Color(Number(color["r"], 0, 1), Number(color["g"], 0, 1), Number(color["b"], 0, 1), Number(color["a"], 1, 1));
            }
            for (int i = 1; i < result.Points.Length; i++)
                if (JToken.DeepEquals(result.Points[i - 1].ToJson(), result.Points[i].ToJson())) throw new RoomToolException("LINE_ENDPOINT_INVALID", "Consecutive endpoints must differ.");
            return result;
        }
    }
}
