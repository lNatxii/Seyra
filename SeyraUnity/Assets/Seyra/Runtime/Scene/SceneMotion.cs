using System;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        const string MotionDrivenMessage = "This object is animated or follows a behavior. Remove its motion or behavior first, or move its parent.";

        GameObject MotionObject(JToken token, bool allowRoot = false)
        {
            GameObject go;
            try { go = Find(token); }
            catch (ArgumentException) { throw new RoomToolException("MOTION_TARGET_INVALID", "Motion requires a live managed object inside Generated."); }
            var panel = go.GetComponentInParent<ManagedPanel>(true);
            if (go.GetComponent<GeneratedObject>() == null || !go.activeInHierarchy || IsDeleted(go)
                || (go.transform == generated ? !allowRoot : !go.transform.IsChildOf(generated))
                || (panel != null && panel.gameObject != go))
                throw new RoomToolException("MOTION_TARGET_INVALID", "Motion requires a live managed object inside Generated.");
            return go;
        }

        JObject MotionSet(JObject args)
        {
            Keys(args, new[] { "entityId", "motion" }, new[] { "paused" });
            var go = MotionObject(args["entityId"]);
            if (go.GetComponent<ManagedLine>() != null || go.GetComponent<SeyraLayoutGroup>() != null
                || (go.transform.parent != null && go.transform.parent.GetComponent<SeyraLayoutGroup>() != null))
                throw new RoomToolException("MOTION_TARGET_INVALID", "Lines, layouts and direct layout children cannot have motion.");
            var spec = MotionSpec.Parse(args["motion"]);
            bool? paused = args["paused"] == null ? (bool?)null : Bool(args, "paused");
            Transform pivot = null;
            if (spec is PositionSpec position && position.Kind == "orbit")
            {
                if (position.PivotId != null)
                {
                    GameObject pivotObject;
                    try { pivotObject = MotionObject(new JValue(position.PivotId)); }
                    catch (RoomToolException) { throw new RoomToolException("MOTION_PIVOT_INVALID", "Orbit pivot must be live managed content inside Generated."); }
                    pivot = pivotObject.transform;
                    if (pivotObject.GetComponent<ManagedLine>() != null || pivot == go.transform || pivot.IsChildOf(go.transform))
                        throw new RoomToolException("MOTION_PIVOT_INVALID", "Orbit pivot cannot be a line, the target itself or its descendant.");
                }
                Invertible(go.transform.parent);
            }
            var motion = go.GetComponent<SeyraMotion>();
            bool added = motion == null;
            if (added) motion = go.AddComponent<SeyraMotion>();
            try { motion.Configure(spec, pivot, paused); }
            catch { if (added) Object.DestroyImmediate(motion); throw; }
            return Inspect(go);
        }

        JObject MotionRemove(JObject args)
        {
            Keys(args, new[] { "entityId", "kind" });
            string kind = Text(args, "kind");
            if (kind != "spin" && kind != "orbit" && kind != "bob") throw new ArgumentException("kind must be spin, orbit or bob.");
            var go = MotionObject(args["entityId"]);
            var motion = go.GetComponent<SeyraMotion>();
            if (motion == null) throw new RoomToolException("MOTION_NOT_FOUND", "The object does not have that motion kind.");
            motion.Remove(kind);
            if (!motion.HasMotion) Object.DestroyImmediate(motion);
            return Inspect(go);
        }

        JObject MotionControl(JObject args)
        {
            Keys(args, new[] { "action" }, new[] { "entityId" });
            string action = Text(args, "action");
            if (action != "pause" && action != "resume" && action != "reset") throw new ArgumentException("action must be pause, resume or reset.");
            var root = args["entityId"] == null ? generated : MotionObject(args["entityId"], true).transform;
            int affected = 0;
            foreach (var motion in root.GetComponentsInChildren<SeyraMotion>(true))
            {
                if (!motion.HasMotion || !motion.gameObject.activeInHierarchy || IsDeleted(motion.gameObject)) continue;
                if (action == "reset") motion.ResetMotion();
                else motion.Paused = action == "pause";
                affected++;
            }
            return new JObject { ["affected"] = affected };
        }

        static JObject MotionsJson(GameObject go)
        {
            var motion = go.GetComponent<SeyraMotion>();
            if (motion == null || !motion.HasMotion) return null;
            var state = motion.Capture();
            var spin = state.Spin?.ToJson();
            if (spin != null) { spin.Remove("kind"); spin["angle"] = state.SpinAngle; }
            var position = state.Position?.ToJson();
            if (position != null)
            {
                if (state.Position.Kind == "orbit") position["angle"] = state.PositionAngle;
                else position["time"] = state.Time;
            }
            string status = motion.Status;
            if (state.Position?.Kind == "orbit" && state.Position.PivotId != null
                && (state.Pivot == null || !state.Pivot.gameObject.activeInHierarchy)) status = "pivot_missing";
            else if (status != "held" && status != "pivot_invalid") status = state.Paused ? "paused" : "running";
            return new JObject { ["paused"] = state.Paused, ["spin"] = spin, ["position"] = position, ["status"] = status };
        }
    }
}
