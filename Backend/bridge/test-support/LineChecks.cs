using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.Spatial;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LineChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("__LineChecks_" + Guid.NewGuid().ToString("N"));
        var root = new GameObject("__LineChecks"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/lines-" + Guid.NewGuid().ToString("N")));
        var service = new SceneToolService(root.transform, storage, AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset"));
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        JObject Ok(string tool, JObject args = null)
        {
            var response = JObject.Parse(service.Dispatch(new JObject { ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = tool,
                ["arguments"] = args ?? new JObject(), ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
            Check((bool)response["success"], response.ToString()); return (JObject)response["data"];
        }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        GameObject Find(string id) => root.GetComponentsInChildren<GeneratedObject>(true).First(r => r.EntityId == id && !r.transform.IsChildOf(root.transform.Find("__SeyraTrash"))).gameObject;
        JObject Get(string id) => Ok("scene_get", new JObject { ["entityId"] = id });
        GameObject head = null;
        try
        {
            // 1. Surface placement and exact initial world diameter.
            var sphere = Ok("create_primitive", new JObject { ["name"] = "Sphere", ["parentId"] = "Generated", ["primitiveType"] = "sphere", ["position"] = V(), ["rotation"] = V(), ["scale"] = V(.1f, .1f, .1f) });
            string sphereId = (string)sphere["entityId"];
            string pointId = (string)Ok("create_point", new JObject { ["name"] = "Point", ["targetId"] = sphereId, ["radius"] = .01 })["entityId"];
            var point = Find(pointId); var target = Find(sphereId);
            Check(Mathf.Abs(Vector3.Distance(point.transform.position, target.transform.position) - .05f) < .001f, "Point center must be on sphere surface.");
            Check((point.GetComponent<Renderer>().bounds.size - Vector3.one * .02f).magnitude < .001f, "Point diameter must be .02 m.");
            // 2. Follow the target and end on the panel perimeter.
            var panel = new JObject { ["version"] = 1, ["kind"] = "label", ["title"] = "", ["text"] = "Sphere", ["width"] = .2, ["height"] = .06, ["fontSize"] = 24 };
            string labelId = (string)Ok("create_panel", new JObject { ["name"] = "Label", ["parentId"] = "Generated", ["position"] = V(0, .2f), ["rotation"] = V(), ["scale"] = V(1, 1, 1), ["panel"] = panel })["entityId"];
            var lineContent = new JObject { ["points"] = new JArray(new JObject { ["entityId"] = pointId }, new JObject { ["entityId"] = labelId }), ["width"] = .003 };
            string lineId = (string)Ok("create_line", new JObject { ["name"] = "Line", ["parentId"] = "Generated", ["line"] = lineContent })["entityId"];
            var line = Find(lineId).GetComponent<ManagedLine>(); var renderer = line.GetComponent<LineRenderer>(); var before = renderer.GetPosition(0);
            Ok("set_transform", new JObject { ["entityId"] = sphereId, ["space"] = "local", ["position"] = V(.03f) });
            await Task.Delay(80); line.Refresh();
            Check(Vector3.Distance(before, renderer.GetPosition(0)) > .02f, "Line must follow the moved point.");
            var surface = Find(labelId).GetComponent<ManagedPanel>().Surface; var local = surface.InverseTransformPoint(renderer.GetPosition(1)); var rect = surface.rect;
            Check(Mathf.Abs(local.z) < .001f && (Mathf.Abs(local.x - rect.xMin) < .001f || Mathf.Abs(local.x - rect.xMax) < .001f || Mathf.Abs(local.y - rect.yMin) < .001f || Mathf.Abs(local.y - rect.yMax) < .001f), "Line must end on label perimeter.");
            // 3. Atomic batch with air placement and bounds.
            var placement = SpatialPlacementService.ForScene(scene); head = new GameObject("__LineAirViewer"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(head, scene);
            head.transform.position = new Vector3(2, 1.6f, 3); head.SetActive(false); var room = head.AddComponent<RoomContext>(); room.enabled = false; room.Configure(null, head.transform);
            typeof(RoomContext).GetField("simulated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(room, true);
            typeof(SpatialPlacementService).GetField("room", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(placement, room);
            int count = (int)Ok("history_list")["total"];
            var built = Ok("scene_build", new JObject { ["name"] = "Labeled sphere", ["nodes"] = new JArray(
                new JObject { ["key"] = "body", ["name"] = "Body", ["type"] = "primitive", ["primitiveType"] = "sphere", ["transform"] = new JObject { ["position"] = V(), ["scale"] = V(.1f, .1f, .1f) } },
                new JObject { ["key"] = "label", ["name"] = "Label", ["type"] = "panel", ["panel"] = panel.DeepClone(), ["transform"] = new JObject { ["position"] = V() }, ["relativeTo"] = new JObject { ["key"] = "body", ["direction"] = "above", ["gap"] = .08 } },
                new JObject { ["key"] = "point", ["name"] = "Point", ["type"] = "point", ["targetKey"] = "body" },
                new JObject { ["key"] = "line", ["name"] = "Line", ["type"] = "line", ["line"] = new JObject { ["points"] = new JArray(new JObject { ["key"] = "point" }, new JObject { ["key"] = "label" }) } }), ["placement"] = new JObject { ["destination"] = "air" } });
            Check((int)Ok("history_list")["total"] == count + 1 && (bool)built["placement"]["applied"], "Air batch must be a single operation.");
            Ok("scene_bounds", new JObject { ["entityId"] = built["rootId"].DeepClone() });
            // 4. Deleted endpoint is hidden and undo restores visibility.
            Ok("delete_object", new JObject { ["entityId"] = sphereId });
            Check((string)Get(lineId)["status"] == "target_missing" && !renderer.enabled, "Deleted target must hide line.");
            Ok("undo", new JObject { ["scope"] = "operation" });
            Check((string)Get(lineId)["status"] == "visible" && renderer.enabled, "Undo must restore line visibility.");
            // 5. Content undo ignores derived positions/material changes.
            var changed = (JObject)lineContent.DeepClone(); changed["width"] = .02; changed["color"] = new JObject { ["r"] = 1, ["g"] = 0, ["b"] = 0, ["a"] = 1 };
            Ok("update_line", new JObject { ["entityId"] = lineId, ["line"] = changed }); Ok("undo", new JObject { ["scope"] = "operation" });
            Check(Mathf.Abs(line.Content.Width - .003f) < .00001f && line.Content.Color == Color.white, "Undo must restore width and color without conflict.");
            // 6. Persistence binds to loaded objects and permits absent endpoints.
            Ok("content_save", new JObject { ["slot"] = "lines" }); Ok("content_load", new JObject { ["slot"] = "lines" });
            line = Find(lineId).GetComponent<ManagedLine>(); renderer = line.GetComponent<LineRenderer>(); before = renderer.GetPosition(0);
            Ok("set_transform", new JObject { ["entityId"] = sphereId, ["space"] = "local", ["position"] = V(.08f) }); await Task.Delay(80); line.Refresh();
            Check((string)Get(lineId)["status"] == "visible" && Vector3.Distance(before, renderer.GetPosition(0)) > .02f, "Loaded line must follow loaded target.");
            Ok("delete_object", new JObject { ["entityId"] = sphereId }); Ok("content_save", new JObject { ["slot"] = "missing" }); Ok("content_load", new JObject { ["slot"] = "missing" });
            Check((string)Get(lineId)["status"] == "target_missing", "Missing saved endpoint must remain loadable.");
            return "Line basic checks passed: " + checks + " (six design scenarios).";
        }
        finally
        {
            if (head != null) Object.DestroyImmediate(head); Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            var unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene); while (!unload.isDone) await Task.Delay(10);
        }
    }
}
