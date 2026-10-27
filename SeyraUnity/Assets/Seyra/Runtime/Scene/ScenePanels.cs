using System;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        readonly UIPrefabCatalog uiCatalog;
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
                go.AddComponent<ManagedPanel>().Initialize(content, uiCatalog);
                AttachRecord(go, "panel", null, group, persistentId);
                RequireCapacity(0);
                go.SetActive(true);
                return go;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
        JObject UpdatePanel(JObject args)
        {
            Keys(args, new[] { "entityId", "panel" });
            var content = PanelContent.Parse(args["panel"]); var go = Find(args["entityId"]);
            var panel = go.GetComponent<ManagedPanel>(); var record = go.GetComponent<GeneratedObject>();
            if (panel == null || record == null || record.Kind != "panel" || !go.transform.IsChildOf(generated))
                throw new ArgumentException("update_panel requires a live managed panel inside Generated.");
            panel.RequireIntact(); panel.Apply(content); return Inspect(go);
        }
    }
}
