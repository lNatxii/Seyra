using System;
using System.Linq;
using Seyra.UI.Presentations;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        readonly UIPrefabCatalog uiCatalog;
        internal void DiscardPanel(GameObject go)
        {
            if (disposed) return;
            try { ExecuteRecorded("delete_object", new JObject { ["entityId"] = Id(go) }); }
            catch (Exception error) { Debug.LogWarning("Seyra: could not discard the panel: " + error.Message); }
        }
        static void RequirePanelRoot(GameObject go)
        {
            var owner = go.GetComponentInParent<ManagedPanel>(true);
            if (owner != null && owner.gameObject != go) throw new ArgumentException("Panel internals are read-only. Use the panel entityId and update_panel.");
        }
        JObject CreatePanel(JObject args)
        {
            Keys(args, new[] { "name", "parentId", "position", "rotation", "scale", "panel" });
            var content = PanelContent.Parse(args["panel"]);
            var parent = Parent(args["parentId"]);
            if ((string)args["parentId"] == "Generated" && activeRequest != null) parent = Find(new JValue(activeRequest));
            var position = ReadVector(args["position"]); var rotation = Quaternion.Euler(ReadVector(args["rotation"])); var scale = ReadVector(args["scale"]);
            var go = InstantiatePanel(String(args["name"], 256), parent.transform, content, activeRequest, position, rotation, scale);
            return Inspect(go);
        }
        GameObject InstantiatePanel(string name, Transform parent, PanelContent content, string group, Vector3 position, Quaternion rotation, Vector3 scale, string persistentId = null)
        {
            if (uiCatalog == null) throw new InvalidOperationException("Assign the UI catalog in SeyraSceneRuntime before creating or loading panels.");
            if(content.kind=="panel" && UnityEngine.Object.FindFirstObjectByType<Oculus.Interaction.PointableCanvasModule>()==null)
                throw new InvalidOperationException("A PointableCanvasModule is required in the host scene before creating or loading window panels.");
            // Reserve the actual prefab subtree, not just its one logical entity.
            RequireCapacity(uiCatalog.WindowPrefab != null ? uiCatalog.WindowPrefab.GetComponentsInChildren<Transform>(true).Length + 32 : 64);
            var go = new GameObject(name); go.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            try
            {
                go.transform.localPosition = position; go.transform.localRotation = rotation; go.transform.localScale = scale; Check(go.transform);
                var panel = go.AddComponent<ManagedPanel>();
                panel.DiscardHandler = DiscardPanel;
                panel.Initialize(content, uiCatalog);
                AttachRecord(go, "panel", null, group, persistentId);
                RequireCapacity(0);
                go.SetActive(true);
                return go;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
        JObject CardChecks(JObject args)
        {
            Keys(args, Array.Empty<string>(), new[] { "rootId" });
            var root = args["rootId"] != null ? Find(args["rootId"]) : generated.gameObject;
            if (root.transform != generated && (!root.transform.IsChildOf(generated) || root.GetComponent<GeneratedObject>() == null))
                throw new ArgumentException("rootId requires a live managed object inside Generated.");
            var cards = new JArray();
            foreach (var transform in LiveContent().Where(t => t == root.transform || t.IsChildOf(root.transform)))
            {
                var panel = transform.GetComponent<ManagedPanel>();
                if (panel == null) continue;
                string title, label; bool completed;
                if (panel.IsPresentation)
                {
                    var data = panel.Presentation.Data;
                    var check = UIPresentationCodec.CardCheck(data);
                    if (check == null) continue;
                    title = data.title; label = check.label;
                    completed = (string)panel.InteractionState?["values"]?["checked"] == "true";
                }
                else
                {
                    var content = panel.Content;
                    if (!content.HasCheck) continue;
                    title = content.title; label = content.checkLabel; completed = content.checkCompleted;
                }
                cards.Add(new JObject { ["entityId"] = Id(transform.gameObject), ["name"] = transform.name,
                    ["title"] = title, ["label"] = label, ["completed"] = completed, ["kind"] = panel.IsPresentation ? "presentation" : "panel" });
            }
            return new JObject { ["cards"] = cards, ["completedCount"] = cards.Count(c => (bool)c["completed"]), ["total"] = cards.Count };
        }
        JObject UpdatePanel(JObject args)
        {
            Keys(args, new[] { "entityId", "panel" });
            var go = Find(args["entityId"]);
            var panel = go.GetComponent<ManagedPanel>(); var record = go.GetComponent<GeneratedObject>();
            if (panel == null || record == null || record.Kind != "panel" || !go.transform.IsChildOf(generated))
                throw new ArgumentException("update_panel requires a live managed panel inside Generated.");
            var json = args["panel"]?.DeepClone();
            if (json?["check"] is JObject check && check.Property("completed") == null && panel.Content.HasCheck)
                check["completed"] = panel.Content.checkCompleted;
            var content = PanelContent.Parse(json);
            panel.RequireIntact(); panel.Apply(content); return Inspect(go);
        }
    }
}
