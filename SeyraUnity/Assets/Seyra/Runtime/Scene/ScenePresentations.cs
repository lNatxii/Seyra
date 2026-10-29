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
                Keys(args, new[] { "name", "parentId", "position", "rotation", "scale", "width", "height", "presentation" }, new[] { "media" });
                RegisterMediaArguments(args);
                var spec = PresentationArguments(args);
                var live = LiveContent().Select(t => t.GetComponent<ManagedPanel>()).Where(p => p != null && p.IsPresentation).ToArray();
                if (live.Length >= 16 || live.Any(p => p.Presentation.Data.id == spec.Data.id)) throw new ArgumentException("Presentation ID already exists or the limit of 16 live presentations was reached.");
                var parent = Parent(args["parentId"]);
                if ((string)args["parentId"] == "Generated" && activeRequest != null) parent = Find(new JValue(activeRequest));
                return Inspect(InstantiatePresentation(String(args["name"], 256), parent.transform, spec, null, activeRequest, ReadVector(args["position"]), Quaternion.Euler(ReadVector(args["rotation"])), ReadVector(args["scale"])));
            }
            if (tool == "ui_update")
            {
                Keys(args, new[] { "entityId", "width", "height", "presentation" }, new[] { "media" });
                RegisterMediaArguments(args);
                var target = PresentationTarget(args); var spec = PresentationArguments(args);
                if (spec.Data.id != target.Presentation.Data.id) throw new ArgumentException("An update must preserve the presentation content ID.");
                target.RequireIntact(); target.ApplyPresentation(spec); return Inspect(target.gameObject);
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
