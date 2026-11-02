using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        static readonly HashSet<string> batchEdits = new HashSet<string> { "set_transform", "set_appearance", "rename_object", "update_panel", "update_line", "set_parent", "delete_object", "place_relative", "motion_set", "motion_remove", "interaction_set" };

        static MotionSpec ParseBatchMotion(JToken token)
        {
            var motion = token as JObject ?? throw new ArgumentException("A motion must be an object.");
            if (motion["pivotKey"] == null) return MotionSpec.Parse(motion);
            if ((string)motion["kind"] != "orbit" || motion["pivotId"] != null || motion["pivotPoint"] != null
                || motion["pivotKey"].Type != JTokenType.String || !Regex.IsMatch((string)motion["pivotKey"], "^[a-z][a-z0-9_]{0,47}$"))
                throw new ArgumentException("Orbit requires exactly one valid pivotKey, pivotId or pivotPoint.");
            var preview = (JObject)motion.DeepClone();
            preview.Remove("pivotKey"); preview["pivotId"] = "batch_pivot";
            return MotionSpec.Parse(preview);
        }

        JObject BatchCall(string tool, JObject args)
        {
            ProtectRoomMutation(tool, args);
            ProtectSpatialApplication(tool, args);
            return ExecuteLifecycle(tool, args);
        }

        JObject ExecuteBatch(string tool, JObject args)
        {
            if (tool == "scene_edit")
            {
                Keys(args, new[] { "operations" });
                var operations = args["operations"] as JArray ?? throw new ArgumentException("operations must be an array.");
                if (operations.Count < 1 || operations.Count > 64) throw new ArgumentException("Expected 1..64 operations.");
                var results = new JArray();
                for (int index = 0; index < operations.Count; index++)
                {
                    try
                    {
                        var operation = operations[index] as JObject ?? throw new ArgumentException("An operation must be an object.");
                        Keys(operation, new[] { "tool", "arguments" });
                        string name = Text(operation, "tool");
                        if (!batchEdits.Contains(name)) throw new ArgumentException("Unsupported scene_edit tool.");
                        var arguments = operation["arguments"] as JObject ?? throw new ArgumentException("arguments must be an object.");
                        ProtectRoomMutation(name, arguments);
                        ProtectSpatialApplication(name, arguments);
                        var target = Find(arguments["entityId"]);
                        if (target.transform == generated || !target.transform.IsChildOf(generated)) throw new ArgumentException("scene_edit requires a live descendant of Generated.");
                        var result = ExecuteLifecycle(name, arguments);
                        result["index"] = index;
                        result["tool"] = name;
                        results.Add(result);
                    }
                    catch (Exception error) { error.Data["sceneBatchIndex"] = index; throw; }
                }
                return new JObject { ["count"] = results.Count, ["results"] = results };
            }

            Keys(args, new[] { "name", "nodes" }, new[] { "parentId", "placement" });
            string groupName = String(args["name"], 256);
            var nodes = args["nodes"] as JArray ?? throw new ArgumentException("nodes must be an array.");
            var ordered = new List<(JObject node, string parent, string[] ancestors)>();
            var byKey = new Dictionary<string, (JObject node, string[] ancestors)>(StringComparer.Ordinal);
            if (nodes.Count < 1 || nodes.Count > 64) throw new ArgumentException("Expected 1..64 nodes.");
            foreach (var token in nodes)
            {
                var node = token as JObject ?? throw new ArgumentException("A node must be an object.");
                string kind = Text(node, "type"), key = Text(node, "key");
                if (!Regex.IsMatch(key, "^[a-z][a-z0-9_]{0,47}$") || byKey.ContainsKey(key)) throw new ArgumentException("Invalid or duplicate node key.");
                var required = new[] { "key", "name", "type", "transform" };
                if (kind == "point" || kind == "line")
                {
                    Keys(node, kind == "point" ? new[] { "key", "name", "type" } : new[] { "key", "name", "type", "line" },
                        kind == "point" ? new[] { "targetKey", "targetId", "direction", "radius", "appearance" } : new[] { "parentKey" });
                    if (kind == "point" && (node["targetKey"] == null) == (node["targetId"] == null)) throw new ArgumentException("Use exactly one point target.");
                    if (node["radius"] != null) LineContent.Number(node["radius"], .002, .1);
                    if (node["direction"] != null && ReadVector(node["direction"]).Equals(UnityEngine.Vector3.zero)) throw new ArgumentException("direction must be non-zero.");
                    if (kind == "line")
                    {
                        var preview = (JObject)(node["line"] as JObject ?? throw new ArgumentException("line must be an object.")).DeepClone();
                        if (preview["points"] is JArray endpoints) foreach (var endpoint in endpoints.OfType<JObject>())
                            if (endpoint["key"] != null) { Keys(endpoint, new[] { "key" }); endpoint["entityId"] = endpoint["key"].DeepClone(); endpoint.Remove("key"); }
                        LineContent.Parse(preview);
                    }
                }
                else
                {
                    if (kind == "primitive") required = required.Concat(new[] { "primitiveType" }).ToArray();
                    else if (kind == "panel") required = required.Concat(new[] { "panel" }).ToArray();
                    else if (kind != "empty") throw new ArgumentException("Unsupported node type.");
                    Keys(node, required, kind == "primitive" ? new[] { "relativeTo", "parentKey", "appearance", "motions", "interaction" } : kind == "empty" ? new[] { "relativeTo", "parentKey", "motions", "interaction" } : new[] { "relativeTo", "parentKey", "motions" });
                    if (node["interaction"] != null) InteractionSpec.Parse(node["interaction"]);
                    var transform = node["transform"] as JObject ?? throw new ArgumentException("transform must be an object.");
                    Keys(transform, new[] { "position" }, new[] { "rotation", "scale" });
                    if (node["motions"] != null)
                    {
                        var motions = node["motions"] as JArray ?? throw new ArgumentException("motions must be an array.");
                        if (motions.Count < 1 || motions.Count > 2) throw new ArgumentException("Expected 1..2 motions.");
                        bool orbit = false, bob = false;
                        foreach (var motion in motions)
                        {
                            string motionKind = ParseBatchMotion(motion).Kind;
                            orbit |= motionKind == "orbit"; bob |= motionKind == "bob";
                        }
                        if (orbit && bob) throw new RoomToolException("MOTION_CHANNEL_BUSY", "Orbit and bob cannot control the same position channel.");
                    }
                }
                string parent = null;
                var ancestors = Array.Empty<string>();
                if (node["parentKey"] != null)
                {
                    parent = String(node["parentKey"], 48);
                    if (!Regex.IsMatch(parent, "^[a-z][a-z0-9_]{0,47}$") || parent == key || !byKey.TryGetValue(parent, out var previous))
                        throw new ArgumentException("parentKey must reference an earlier node.");
                    if (new[] { "panel", "point", "line" }.Contains((string)previous.node["type"])) throw new ArgumentException("A view, point or line cannot be a node parent.");
                    ancestors = previous.ancestors.Concat(new[] { parent }).ToArray();
                }
                if (kind == "point" && node["targetKey"] != null)
                {
                    string target = String(node["targetKey"], 48);
                    if (!byKey.TryGetValue(target, out var previous) || (string)previous.node["type"] != "primitive") throw new ArgumentException("targetKey must reference an earlier primitive.");
                    ancestors = previous.ancestors.Concat(new[] { target }).ToArray();
                }
                if (ancestors.Length >= 6) throw new ArgumentException("Maximum node depth is 6.");
                byKey.Add(key, (node, ancestors));
                ordered.Add((node, parent, ancestors));
            }
            foreach (var entry in ordered)
            {
                if (entry.node["interaction"] != null && entry.ancestors.Any(key => byKey[key].node["interaction"] != null))
                    throw new RoomToolException("INTERACTION_NESTED", "Interactive objects cannot contain other interactive objects.");
                if (entry.node["motions"] is JArray motions)
                    foreach (var motion in motions)
                        if (motion["pivotKey"] != null)
                        {
                            string pivotKey = (string)motion["pivotKey"];
                            string key = Text(entry.node, "key");
                            if (!byKey.TryGetValue(pivotKey, out var pivot) || (string)pivot.node["type"] == "line" || pivotKey == key || pivot.ancestors.Contains(key))
                                throw new RoomToolException("MOTION_PIVOT_INVALID", "pivotKey must reference a non-line node other than self or descendants.");
                        }
                if ((string)entry.node["type"] == "line")
                    foreach (var endpoint in (JArray)entry.node["line"]["points"])
                        if (endpoint["key"] != null && (!byKey.TryGetValue((string)endpoint["key"], out var target) || (string)target.node["type"] == "line")) throw new ArgumentException("Line keys must reference a non-line node.");
                if (entry.node["relativeTo"] == null) continue;
                var relative = entry.node["relativeTo"] as JObject ?? throw new ArgumentException("relativeTo must be an object.");
                Keys(relative, new[] { "direction", "gap" }, new[] { "key", "entityId" });
                if ((relative["key"] == null) == (relative["entityId"] == null)) throw new ArgumentException("Use key or entityId.");
                double gap = relative["gap"].Type == JTokenType.Float || relative["gap"].Type == JTokenType.Integer ? (double)relative["gap"] : double.NaN;
                if (double.IsNaN(gap) || double.IsInfinity(gap) || gap < 0 || gap > 10) throw new ArgumentException("gap must be in [0,10].");
                if (relative["key"] != null)
                {
                    string reference = String(relative["key"], 48), key = Text(entry.node, "key");
                    if (!byKey.TryGetValue(reference, out var other) || reference == key || entry.ancestors.Contains(reference) || other.ancestors.Contains(key)) throw new ArgumentException("relativeTo must reference a disjoint node.");
                    if (other.node["relativeTo"] != null) throw new ArgumentException("relativeTo chains are not supported.");
                    if (new[] { "point", "line" }.Contains((string)other.node["type"])) throw new ArgumentException("relativeTo cannot target a point or line.");
                }
            }
            JObject placement = null;
            if (args["placement"] != null)
            {
                placement = args["placement"] as JObject ?? throw new ArgumentException("placement must be an object.");
                Keys(placement, new[] { "destination" }, new[] { "physicalId", "allowAirFallback", "minScale" });
                if (Text(placement, "destination") == "air" && placement["physicalId"] != null) throw new ArgumentException("Air has no physical support.");
            }
            RequireCapacity(2 + ordered.Sum(entry => ((string)entry.node["type"] == "panel" ? 64 : 1) + (entry.node["interaction"]?["detail"] != null ? 64 : 0)));
            bool automatic = activeRequest == null;
            string requestId = activeRequest;
            try
            {
                if (automatic) { BatchCall("request_begin", new JObject { ["name"] = groupName }); requestId = activeRequest; }
                JObject Creation(string name, JToken parent, JObject transform) => new JObject {
                    ["name"] = name, ["parentId"] = parent.DeepClone(), ["position"] = transform["position"].DeepClone(),
                    ["rotation"] = transform["rotation"]?.DeepClone() ?? new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
                    ["scale"] = transform["scale"]?.DeepClone() ?? new JObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 } };
                var root = BatchCall("create_gameobject", Creation(groupName, args["parentId"] ?? new JValue("Generated"), new JObject { ["position"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } }));
                string rootId = (string)root["entityId"];
                var ids = new JObject();
                foreach (var entry in ordered)
                {
                    var node = entry.node;
                    string kind = Text(node, "type"), key = Text(node, "key");
                    if (kind == "point" || kind == "line") continue;
                    var creation = Creation(String(node["name"], 256), entry.parent == null ? new JValue(rootId) : ids[entry.parent], (JObject)node["transform"]);
                    if (kind == "primitive") creation["primitiveType"] = node["primitiveType"].DeepClone();
                    if (kind == "panel") creation["panel"] = node["panel"].DeepClone();
                    var created = BatchCall(kind == "empty" ? "create_gameobject" : kind == "primitive" ? "create_primitive" : "create_panel", creation);
                    ids[key] = created["entityId"].DeepClone();
                    if (node["appearance"] != null)
                    {
                        var appearance = node["appearance"] as JObject ?? throw new ArgumentException("appearance must be an object.");
                        Keys(appearance, new[] { "style", "color" });
                        appearance = (JObject)appearance.DeepClone(); appearance["entityId"] = ids[key].DeepClone();
                        BatchCall("set_appearance", appearance);
                    }
                }
                foreach (var entry in ordered)
                {
                    if (!(entry.node["relativeTo"] is JObject relative)) continue;
                    BatchCall("place_relative", new JObject { ["entityId"] = ids[Text(entry.node, "key")].DeepClone(),
                        ["referenceId"] = relative["key"] != null ? ids[(string)relative["key"]].DeepClone() : relative["entityId"].DeepClone(),
                        ["direction"] = relative["direction"].DeepClone(), ["gap"] = relative["gap"].DeepClone(), ["frame"] = "world" });
                }
                foreach (var entry in ordered.Where(e => (string)e.node["type"] == "point"))
                {
                    var node = entry.node;
                    var point = new JObject { ["name"] = node["name"].DeepClone(), ["targetId"] = node["targetKey"] != null ? ids[(string)node["targetKey"]].DeepClone() : node["targetId"].DeepClone() };
                    foreach (string field in new[] { "direction", "radius", "appearance" }) if (node[field] != null) point[field] = node[field].DeepClone();
                    ids[Text(node, "key")] = BatchCall("create_point", point)["entityId"].DeepClone();
                }
                foreach (var entry in ordered.Where(e => (string)e.node["type"] == "line"))
                {
                    var node = entry.node; var line = (JObject)node["line"].DeepClone();
                    foreach (var endpoint in ((JArray)line["points"]).Cast<JObject>())
                        if (endpoint["key"] != null) { endpoint["entityId"] = ids[(string)endpoint["key"]].DeepClone(); endpoint.Remove("key"); }
                    ids[Text(node, "key")] = BatchCall("create_line", new JObject { ["name"] = node["name"].DeepClone(), ["parentId"] = entry.parent == null ? new JValue(rootId) : ids[entry.parent].DeepClone(), ["line"] = line })["entityId"].DeepClone();
                }
                // Resolve pivots only after all node types exist, including forward point references.
                // Motion must be configured before placement so the planner reserves its sweep.
                foreach (var entry in ordered)
                    if (entry.node["motions"] is JArray motions)
                        foreach (var token in motions)
                        {
                            var motion = (JObject)token.DeepClone();
                            if (motion["pivotKey"] != null)
                            {
                                motion["pivotId"] = ids[(string)motion["pivotKey"]].DeepClone();
                                motion.Remove("pivotKey");
                            }
                            BatchCall("motion_set", new JObject { ["entityId"] = ids[Text(entry.node, "key")].DeepClone(), ["motion"] = motion });
                        }
                var result = new JObject { ["rootId"] = rootId, ["entityId"] = rootId, ["requestGroupId"] = requestId, ["nodes"] = ids };
                if (placement != null)
                {
                    placement = (JObject)placement.DeepClone(); placement["entityId"] = rootId;
                    var placed = BatchCall("spatial_place", placement);
                    if ((bool?)placed["applied"] != true)
                    {
                        string message = (string)placed["reason"] ?? "No compatible placement.";
                        if (placed["rejected"] is JArray rejected && rejected.Count > 0)
                        {
                            var reasons = rejected.GroupBy(item => (string)item["reason"], StringComparer.Ordinal)
                                .Select(group => group.Key + " x" + group.Count() + " (" + string.Join(", ", group.Select(item => (string)item["physicalId"] ?? "air").Distinct(StringComparer.Ordinal).Take(4)) + ")");
                            message += " Rejected: " + string.Join("; ", reasons) + ".";
                        }
                        throw new RoomToolException("PLACEMENT_REJECTED", message.Length > 1024 ? message.Substring(0, 1024) : message);
                    }
                    result["placement"] = placed;
                }
                var details = new JObject();
                foreach (var entry in ordered)
                    if (entry.node["interaction"] is JObject fields)
                    {
                        var interaction = (JObject)fields.DeepClone();
                        string key = Text(entry.node, "key");
                        interaction["entityId"] = ids[key].DeepClone(); interaction["interactive"] = true;
                        var configured = BatchCall("interaction_set", interaction);
                        if (configured["interaction"]?["detailId"]?.Type == JTokenType.String) details[key] = configured["interaction"]["detailId"].DeepClone();
                    }
                result["details"] = details;
                return result;
            }
            finally { if (automatic) activeRequest = null; }
        }
    }
}
