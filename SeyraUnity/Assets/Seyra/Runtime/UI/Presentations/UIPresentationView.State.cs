using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    public sealed partial class UIPresentationView
    {
        readonly Dictionary<string, Action<string>> valueBindings = new Dictionary<string, Action<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, Action<int>> zoomBindings = new Dictionary<string, Action<int>>(StringComparer.Ordinal);
        readonly Dictionary<string, int> zoomValues = new Dictionary<string, int>(StringComparer.Ordinal);
        int photoIndex;
        public Action<int> RestorePage { private get; set; }
        public Action<string> RestoreNode { private get; set; }
        public event Action Restored;
        public void BindValue(string id, Action<string> apply) => valueBindings.Add(id, apply);
        public string RegisterPhoto() => (photoIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        public void BindZoom(string key, Action<int> apply) { zoomBindings.Add(key, apply); zoomValues.Add(key, 1); }
        public void SetZoom(string key, int value) => zoomValues[key] = value;
        public JObject ExportState(bool visible)
        {
            var position = scroll.normalizedPosition;
            var result = new JObject { ["version"] = 1, ["revision"] = Revision, ["index"] = Index, ["nodeId"] = NodeId == null ? JValue.CreateNull() : new JValue(NodeId),
                ["values"] = JObject.FromObject(Values), ["submitted"] = Submitted, ["submission"] = Submitted ? Submission?.DeepClone() : null,
                ["lastAction"] = LastAction, ["scroll"] = new JObject { ["x"] = Mathf.Clamp01(position.x), ["y"] = Mathf.Clamp01(position.y) },
                ["zoom"] = JObject.FromObject(zoomValues), ["visible"] = visible };
            return UIPresentationState.Validate(Data, result);
        }
        public void RestoreState(JObject state)
        {
            state = UIPresentationState.Validate(Data, state);
            Values.Clear();
            foreach (var property in ((JObject)state["values"]).Properties()) Values[property.Name] = (string)property.Value;
            foreach (var pair in Values) if (valueBindings.TryGetValue(pair.Key, out var apply)) apply(pair.Value);
            if (RestoreNode != null) RestoreNode((string)state["nodeId"]);
            else RestorePage?.Invoke((int)state["index"]);
            foreach (var property in ((JObject)state["zoom"]).Properties())
                if (zoomBindings.TryGetValue(property.Name, out var apply)) { apply((int)property.Value); zoomValues[property.Name] = (int)property.Value; }
            Revision = (int)state["revision"]; LastAction = (string)state["lastAction"]; Submitted = (bool)state["submitted"];
            Submission = state["submission"] as JObject;
            Restored?.Invoke();
            Canvas.ForceUpdateCanvases(); UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            scroll.StopMovement(); scroll.normalizedPosition = new Vector2((float)state["scroll"]["x"], (float)state["scroll"]["y"]);
        }
    }
}
