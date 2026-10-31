using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        Transform[] ResolveLineTargets(LineContent content, GameObject owner = null)
        {
            return content.Points.Select(p => {
                if (p.EntityId == null) return null;
                GameObject target;
                try { target = Find(new JValue(p.EntityId)); }
                catch (ArgumentException) { throw new RoomToolException("LINE_ENDPOINT_INVALID", "Endpoint must identify live managed content."); }
                if (target == owner || target.transform == generated || !target.transform.IsChildOf(generated) || target.GetComponent<GeneratedObject>() == null || target.GetComponent<ManagedLine>() != null || !target.activeInHierarchy)
                    throw new RoomToolException("LINE_ENDPOINT_INVALID", "Endpoint must identify live managed content other than a line.");
                RequirePanelRoot(target);
                return target.transform;
            }).ToArray();
        }
        void RequireLineCapacity()
        {
            if (LiveContent().Count(t => t.GetComponent<ManagedLine>() != null) >= 128) throw new RoomToolException("LINE_LIMIT", "At most 128 live lines are allowed.");
        }
        GameObject InstantiateLine(string name, Transform parent, LineContent content, Transform[] targets, string group, string persistentId = null)
        {
            if (persistentId == null) RequireLineCapacity(); RequireCapacity(1);
            var go = new GameObject(name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            try
            {
                go.AddComponent<ManagedLine>().Restore(content, targets);
                AttachRecord(go, "line", null, group, persistentId);
                return go;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
        JObject CreatePoint(JObject args)
        {
            Keys(args, new[] { "name", "targetId" }, new[] { "direction", "radius", "appearance" });
            GameObject target;
            try { target = Find(args["targetId"]); }
            catch (ArgumentException) { throw new RoomToolException("POINT_TARGET_INVALID", "Point target must be a live managed primitive."); }
            var collider = target.GetComponent<Collider>();
            if (target.GetComponent<GeneratedObject>()?.Kind != "primitive" || !target.transform.IsChildOf(generated) || collider == null || !collider.enabled || !target.activeInHierarchy)
                throw new RoomToolException("POINT_TARGET_INVALID", "Point target requires an active native primitive collider.");
            for (var ancestor = target.transform.parent; ancestor != null; ancestor = ancestor.parent)
            {
                var scale = ancestor.localScale;
                if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0 || Mathf.Abs(scale.x - scale.y) > .00001f * scale.x || Mathf.Abs(scale.x - scale.z) > .00001f * scale.x)
                    throw new RoomToolException("POINT_TARGET_INVALID", "Point target ancestors require positive uniform scales.");
            }
            var size = target.transform.lossyScale;
            if (size.x == 0 || size.y == 0 || size.z == 0) throw new RoomToolException("POINT_TARGET_INVALID", "Point target scale must be invertible.");
            var direction = args["direction"] != null ? ReadVector(args["direction"]) : Vector3.up;
            // Normalize after rescaling to avoid overflow for finite float vectors.
            float largest = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (largest == 0) throw new ArgumentException("direction must be non-zero.");
            direction = (direction / largest).normalized;
            float radius = args["radius"] != null ? LineContent.Number(args["radius"], .002, .1) : .01f;
            if (args["appearance"] != null) Keys(args["appearance"] as JObject ?? throw new ArgumentException("appearance must be an object."), new[] { "style", "color" });
            var appearance = args["appearance"] != null ? ReadAppearance((JObject)args["appearance"]) : (style: AppearanceStyle.Unlit, color: Color.white);
            Physics.SyncTransforms();
            var bounds = collider.bounds; float distance = 2 * bounds.extents.magnitude + .01f;
            if (!collider.Raycast(new Ray(bounds.center + direction * distance, -direction), out var hit, distance * 2))
                throw new RoomToolException("POINT_SURFACE_NOT_FOUND", "No collider surface in that direction; choose another direction.");
            RequireCapacity(1);
            var go = InstantiateManaged(String(args["name"], 256), target.transform, "primitive", "sphere", activeRequest,
                target.transform.InverseTransformPoint(hit.point), Quaternion.identity,
                new Vector3(2 * radius / Mathf.Abs(size.x), 2 * radius / Mathf.Abs(size.y), 2 * radius / Mathf.Abs(size.z)));
            go.GetComponent<ManagedAppearance>().SetAppearance(appearance.style, appearance.color);
            var result = Inspect(go); result["primitiveType"] = "sphere"; result["targetId"] = Id(target); return result;
        }
        JObject CreateLine(JObject args)
        {
            Keys(args, new[] { "name", "parentId", "line" });
            var content = LineContent.Parse(args["line"]); var targets = ResolveLineTargets(content);
            var parent = Parent(args["parentId"]);
            if ((string)args["parentId"] == "Generated" && activeRequest != null) parent = Find(new JValue(activeRequest));
            return Inspect(InstantiateLine(String(args["name"], 256), parent.transform, content, targets, activeRequest));
        }
        JObject UpdateLine(JObject args)
        {
            Keys(args, new[] { "entityId", "line" });
            var go = Find(args["entityId"]); var line = go.GetComponent<ManagedLine>();
            if (line == null || !go.transform.IsChildOf(generated) || IsDeleted(go)) throw new ArgumentException("update_line requires a live managed line.");
            var content = LineContent.Parse(args["line"]); var targets = ResolveLineTargets(content, go);
            line.Restore(content, targets); return Inspect(go);
        }
    }
}
