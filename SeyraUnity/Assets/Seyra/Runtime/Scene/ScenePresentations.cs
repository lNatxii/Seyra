using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Presentations;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        readonly UIPresentationCatalog presentations;
        readonly RemoteMediaStore remoteMedia;
        static string[] MediaIds(JToken token, string field)
        {
            var ids = token as JArray ?? throw new ArgumentException(field + " must be an array.");
            return ids.Select(id => id.Type == JTokenType.String &&
                System.Text.RegularExpressions.Regex.IsMatch((string)id, @"\A(?:web|gen):[0-9a-f]{32}\z")
                ? (string)id : throw new ArgumentException("Invalid media ID in " + field + ".")).Distinct(StringComparer.Ordinal).ToArray();
        }
        static string[] PendingMediaArguments(JObject args) => args["pendingMedia"] == null ? Array.Empty<string>() : MediaIds(args["pendingMedia"], "pendingMedia");
        void ApplyPendingMedia(string[] ids)
        {
            remoteMedia.MarkPending(ids);
            remoteMedia.RefreshPanels(ids);
        }
        void RegisterMediaArguments(JObject args)
        {
            if (args["media"] != null)
                remoteMedia.Register(args["media"] as JArray ?? throw new ArgumentException("media must be an array."));
        }
        ManagedPanel PresentationTarget(JObject args)
        {
            var go = Find(args["entityId"]);
            var panel = go.GetComponent<ManagedPanel>();
            if (panel == null || !panel.IsPresentation || !go.transform.IsChildOf(generated) || go.GetComponent<GeneratedObject>()?.Kind != "presentation")
                throw new ArgumentException("A live managed presentation entityId is required.");
            return panel;
        }
        PresentationContent PresentationArguments(JObject args) => PresentationContent.Parse(new JObject {
            ["version"] = 1, ["width"] = args["width"]?.DeepClone(), ["height"] = args["height"]?.DeepClone(), ["presentation"] = args["presentation"]?.DeepClone() });
        GameObject InstantiatePresentation(string name, Transform parent, PresentationContent spec, JObject state, string group, Vector3 position, Quaternion rotation, Vector3 scale, string persistentId = null)
        {
            if (uiCatalog == null || presentations == null || presentations.Components != uiCatalog) throw new InvalidOperationException("The presentation and component catalogs are missing or incompatible.");
            if (UnityEngine.Object.FindFirstObjectByType<Oculus.Interaction.PointableCanvasModule>() == null) throw new InvalidOperationException("The host scene requires PointableCanvasModule.");
            RequireCapacity(1);
            var go = new GameObject(name); go.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene); go.transform.SetParent(parent, false);
            try
            {
                go.transform.localPosition = position; go.transform.localRotation = rotation; go.transform.localScale = scale; Check(go.transform);
                go.AddComponent<ManagedPanel>().InitializePresentation(spec, uiCatalog, presentations, state);
                AttachRecord(go, "presentation", null, group, persistentId); RequireCapacity(0); go.SetActive(true); return go;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
        JObject ExecutePresentation(string tool, JObject args)
        {
            if (tool == "media_status")
            {
                Keys(args, new[] { "ids" });
                return new JObject { ["existing"] = new JArray(MediaIds(args["ids"], "ids").Where(remoteMedia.Exists)) };
            }
            if (tool == "media_deliver")
            {
                Keys(args, Array.Empty<string>(), new[] { "media", "failed" });
                var failed = args["failed"] == null ? Array.Empty<string>() : MediaIds(args["failed"], "failed");
                var media = args["media"] == null ? null : args["media"] as JArray ?? throw new ArgumentException("media must be an array.");
                if (media != null && media.Count > 1) throw new ArgumentException("media_deliver accepts at most one media entry.");
                int refreshed = media == null ? 0 : remoteMedia.Register(media);
                refreshed += remoteMedia.Fail(failed);
                return new JObject { ["refreshed"] = refreshed };
            }
            if (tool == "ui_catalog")
            {
                Keys(args, Array.Empty<string>());
                return new JObject { ["types"] = new JArray(UIPresentationCodec.Types.Keys), ["contentVersion"] = 1, ["maxPresentations"] = 16,
                    ["minWidth"] = .4, ["maxWidth"] = 2, ["minHeight"] = .4, ["maxHeight"] = 2, ["maxContentBytes"] = 131072,
                    ["mediaSource"] = "registeredAssets", ["placement"] = "explicitGeneratedParent", ["catalogAvailable"] = presentations != null };
            }
            if (tool == "ui_media_list")
            {
                Keys(args, new[] { "offset", "limit" });
                if (presentations == null) throw new InvalidOperationException("The presentation catalog is missing.");
                var media = presentations.Media.Where(m => m.sprite != null).ToArray();
                int offset = Int(args, "offset", 0, 0, 1000000), limit = Int(args, "limit", 20, 1, 100);
                return new JObject { ["total"] = media.Length, ["offset"] = offset, ["hasMore"] = offset + limit < media.Length,
                    ["media"] = new JArray(media.Skip(offset).Take(limit).Select(m => new JObject { ["imageRef"] = m.id, ["description"] = m.description })) };
            }
            if (tool == "ui_list")
            {
                Keys(args, Array.Empty<string>());
                return new JObject { ["presentations"] = new JArray(LiveContent().Select(t => t.GetComponent<ManagedPanel>()).Where(p => p != null && p.IsPresentation)
                    .Select(p => new JObject { ["entityId"] = Id(p.gameObject), ["id"] = p.Presentation.Data.id, ["type"] = p.Presentation.Data.Type,
                        ["title"] = p.Presentation.Data.title, ["interaction"] = p.InteractionState })) };
            }
            if (tool == "ui_present")
            {
                Keys(args, new[] { "name", "parentId", "position", "rotation", "scale", "width", "height", "presentation" }, new[] { "media", "pendingMedia" });
                var pending = PendingMediaArguments(args);
                RegisterMediaArguments(args);
                var spec = PresentationArguments(args);
                var live = LiveContent().Select(t => t.GetComponent<ManagedPanel>()).Where(p => p != null && p.IsPresentation).ToArray();
                if (live.Length >= 16 || live.Any(p => p.Presentation.Data.id == spec.Data.id)) throw new ArgumentException("Presentation ID already exists or the limit of 16 live presentations was reached.");
                var parent = Parent(args["parentId"]);
                if ((string)args["parentId"] == "Generated" && activeRequest != null) parent = Find(new JValue(activeRequest));
                var created = InstantiatePresentation(String(args["name"], 256), parent.transform, spec, null, activeRequest, ReadVector(args["position"]), Quaternion.Euler(ReadVector(args["rotation"])), ReadVector(args["scale"]));
                ApplyPendingMedia(pending);
                return Inspect(created);
            }
            if (tool == "ui_update")
            {
                Keys(args, new[] { "entityId", "width", "height", "presentation" }, new[] { "media", "pendingMedia" });
                var pending = PendingMediaArguments(args);
                RegisterMediaArguments(args);
                var target = PresentationTarget(args);
                var updated = (JObject)args.DeepClone();
                if (updated["presentation"]?["check"] is JObject check && check.Property("completed") == null &&
                    UIPresentationCodec.CardCheck(target.Presentation.Data) != null)
                    check["completed"] = (string)target.InteractionState?["values"]?["checked"] == "true";
                var spec = PresentationArguments(updated);
                if (spec.Data.id != target.Presentation.Data.id) throw new ArgumentException("An update must preserve the presentation content ID.");
                target.RequireIntact(); target.ApplyPresentation(spec); ApplyPendingMedia(pending); return Inspect(target.gameObject);
            }
            Keys(args, new[] { "entityId" });
            var panel = PresentationTarget(args);
            switch (tool)
            {
                case "ui_get": return Inspect(panel.gameObject);
                case "ui_close": panel.SetPresentationVisible(false); return Inspect(panel.gameObject);
                case "ui_open": panel.SetPresentationVisible(true); return Inspect(panel.gameObject);
                case "ui_remove": return ExecuteLifecycle("delete_object", args);
                default: throw new ArgumentException("Unknown presentation tool.");
            }
        }
    }
}
