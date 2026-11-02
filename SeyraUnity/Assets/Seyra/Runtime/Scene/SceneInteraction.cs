using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        static void RequireIndependentDetail(GameObject go)
        {
            if (go.GetComponentInParent<SeyraFollowTarget>(true) != null)
                throw new RoomToolException("INTERACTION_DETAIL_OWNED", "This detail belongs to an interactive object. Change or remove its interaction instead.");
        }
        GameObject InteractionTarget(JToken id)
        {
            GameObject go;
            try { go = Find(id); }
            catch (ArgumentException) { throw new RoomToolException("INTERACTION_UNSUPPORTED", "Interaction requires a live managed primitive or container."); }
            var record = go.GetComponent<GeneratedObject>();
            if (go.transform == generated || !go.transform.IsChildOf(generated) || !go.activeInHierarchy || record == null ||
                (record.Kind != "primitive" && record.Kind != "empty") || go.GetComponent<SeyraLayoutGroup>() != null ||
                go.transform.parent?.GetComponent<SeyraLayoutGroup>() != null || go.GetComponentInParent<ManagedPanel>(true) != null)
                throw new RoomToolException("INTERACTION_UNSUPPORTED", "Interaction requires a live managed primitive or container outside layouts.");
            return go;
        }
        ManagedPanel InteractionDetail(InteractionSpec spec)
        {
            if (string.IsNullOrEmpty(spec?.DetailId)) return null;
            return Find(new JValue(spec.DetailId), true).GetComponent<ManagedPanel>();
        }
        JObject InteractionJson(SeyraInteractive interactive)
        {
            var spec = interactive.Specification;
            if (spec == null) return null;
            var detail = interactive.Detail;
            return new JObject { ["grabbable"] = spec.Grabbable, ["returnOnRelease"] = spec.ReturnOnRelease,
                ["exclusive"] = spec.Exclusive, ["detailId"] = spec.DetailId, ["detailSide"] = spec.DetailSide,
                ["detailGap"] = spec.DetailGap, ["detailVisible"] = detail != null && !IsDeleted(detail.gameObject) && detail.WindowVisible,
                ["status"] = interactive.IsHeld ? "held" : "idle" };
        }
        JObject InteractionSet(JObject args)
        {
            Keys(args, new[] { "entityId", "interactive" }, new[] { "grabbable", "returnOnRelease", "exclusive", "detail", "detailSide", "detailGap", "detailOpen" });
            bool enabled = Bool(args, "interactive");
            if (!enabled && args.Count != 2) throw new ArgumentException("Disabling interaction accepts only entityId and interactive.");
            var go = InteractionTarget(args["entityId"]);
            var current = go.GetComponent<SeyraInteractive>();
            if (!enabled)
            {
                var oldDetail = current?.Detail;
                if (current != null) UnityEngine.Object.DestroyImmediate(current);
                if (oldDetail != null && !IsDeleted(oldDetail.gameObject)) MoveToTrash(oldDetail.gameObject);
                return Inspect(go);
            }
            for (var ancestor = go.transform.parent; ancestor != null && ancestor != generated; ancestor = ancestor.parent)
                if (ancestor.GetComponent<SeyraInteractive>() != null) throw new RoomToolException("INTERACTION_NESTED", "Interactive objects cannot contain other interactive objects.");
            if (go.GetComponentsInChildren<SeyraInteractive>(true).Any(i => i.gameObject != go))
                throw new RoomToolException("INTERACTION_NESTED", "Interactive objects cannot contain other interactive objects.");
            if (current == null && LiveContent().Count(t => t.GetComponent<SeyraInteractive>() != null) >= 32)
                throw new RoomToolException("INTERACTION_LIMIT", "At most 32 live objects may be interactive.");
            var fields = (JObject)args.DeepClone(); fields.Remove("entityId"); fields.Remove("interactive");
            var spec = InteractionSpec.Parse(fields);
            var points = GeometryPoints(go);
            var detail = current?.Detail;
            if (spec.DetailPanel != null || spec.DetailPresentation != null)
            {
                bool presentation = spec.DetailPresentation != null;
                if (detail != null && detail.IsPresentation == presentation)
                {
                    detail.RequireIntact();
                    if (presentation)
                    {
                        if (detail.Presentation.Data.id != spec.DetailPresentation.Data.id) throw new ArgumentException("Updating a detail must preserve its presentation content ID.");
                        detail.ApplyPresentation(spec.DetailPresentation);
                    }
                    else detail.Apply(spec.DetailPanel);
                }
                else
                {
                    var live = LiveContent().Select(t => t.GetComponent<ManagedPanel>()).Where(p => p != null && p.IsPresentation).ToArray();
                    if (presentation && (live.Length >= 16 || live.Any(p => p.Presentation.Data.id == spec.DetailPresentation.Data.id)))
                        throw new ArgumentException("Presentation ID already exists or the limit of 16 live presentations was reached.");
                    if (detail != null && !IsDeleted(detail.gameObject)) MoveToTrash(detail.gameObject);
                    Transform parent = generated;
                    string group = go.GetComponent<GeneratedObject>().RequestGroupId;
                    if (!string.IsNullOrEmpty(group) && objects.TryGetValue(group, out var value) && value is GameObject request && request != null && !IsDeleted(request)) parent = request.transform;
                    var created = presentation
                        ? InstantiatePresentation(go.name + " details", parent, spec.DetailPresentation, null, group, Vector3.zero, Quaternion.identity, Vector3.one)
                        : InstantiatePanel(go.name + " details", parent, spec.DetailPanel, group, Vector3.zero, Quaternion.identity, Vector3.one);
                    detail = created.GetComponent<ManagedPanel>();
                }
            }
            spec.DetailId = detail != null ? Id(detail.gameObject) : null;
            // Content has its own managed entity and history; keep only runtime configuration here.
            spec.DetailPanel = null; spec.DetailPresentation = null;
            if (current == null) current = go.AddComponent<SeyraInteractive>();
            current.Configure(spec, points, detail);
            return Inspect(go);
        }
        void RestoreInteractive(ObjectState state, bool force)
        {
            var go = state.Object;
            var current = go.GetComponent<SeyraInteractive>();
            if (state.Interactive == null)
            {
                if (current != null) UnityEngine.Object.DestroyImmediate(current);
                return;
            }
            var detail = InteractionDetail(state.Interactive);
            bool visible = detail != null && detail.WindowVisible;
            if (force || current == null || current.Specification?.Signature != state.InteractiveSignature ||
                (detail != null && detail.GetComponent<SeyraFollowTarget>()?.Target != go.transform))
            {
                if (current == null) current = go.AddComponent<SeyraInteractive>();
                var spec = state.Interactive.Copy(); spec.DetailOpen = visible;
                current.Configure(spec, GeometryPoints(go), detail);
            }
        }
        void MoveInteractionDetails(GameObject root)
        {
            foreach (var interactive in root.GetComponentsInChildren<SeyraInteractive>(true))
                if (interactive.Detail != null && !IsDeleted(interactive.Detail.gameObject)) MoveToTrash(interactive.Detail.gameObject);
        }
        void RestoreInteractionDetails(GameObject root)
        {
            foreach (var interactive in root.GetComponentsInChildren<SeyraInteractive>(true))
            {
                var detail = interactive.Detail;
                if (detail == null || !IsDeleted(detail.gameObject) || !deletedOrigins.TryGetValue(detail.gameObject, out var origin)) continue;
                origin.Validate();
                if (origin.Parent == null || IsDeleted(origin.Parent.gameObject)) throw new InvalidOperationException("Restore the detail's original parent first.");
                origin.Apply(); detail.transform.SetSiblingIndex(origin.Sibling); detail.gameObject.SetActive(origin.Active);
            }
        }
    }
}
