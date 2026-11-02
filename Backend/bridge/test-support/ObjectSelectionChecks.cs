using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Seyra.Chat;
using Seyra.SceneTools;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ObjectSelectionChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var scene = SceneManager.CreateScene("__ObjectSelectionChecks_" + Guid.NewGuid().ToString("N"));
        var root = new GameObject("__ObjectSelectionChecks"); SceneManager.MoveGameObjectToScene(root, scene);
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/object-selection-" + Guid.NewGuid().ToString("N")));
        var library = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var catalog = Resources.Load<UIPresentationCatalog>("SeyraUIPresentations");
        var service = new SceneToolService(root.transform, storage, library, catalog);
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Ok(string tool, JObject args)
        {
            var result = JObject.Parse(service.Dispatch(new JObject {
                ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(),
                ["tool"] = tool, ["arguments"] = args, ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")
            }.ToString()));
            Check((bool)result["success"], result.ToString()); return (JObject)result["data"];
        }
        GameObject Find(string id) => root.GetComponentsInChildren<GeneratedObject>(true).First(g => g.EntityId == id).gameObject;
        void Tap(ObjectMarkTarget target)
        {
            foreach (var type in new[] { PointerEventType.Hover, PointerEventType.Select, PointerEventType.Unselect, PointerEventType.Unhover })
                target.Ray.PublishPointerEvent(new PointerEvent(1001, type, Pose.identity));
        }
        JObject Label(string key, string text) => new JObject {
            ["key"] = key, ["name"] = text + " label", ["type"] = "panel",
            ["transform"] = new JObject { ["position"] = V(0, 1.3f, 2) },
            ["panel"] = new JObject { ["version"] = 1, ["kind"] = "label", ["chip"] = true,
                ["title"] = "", ["text"] = text, ["width"] = .2, ["height"] = .05, ["fontSize"] = 22 }
        };
        try
        {
            ObjectMarkSelection.ClearCurrent();
            var built = Ok("scene_build", new JObject {
                ["name"] = "Selection marks", ["movable"] = false,
                ["nodes"] = new JArray {
                    new JObject { ["key"] = "boxes", ["name"] = "Boxes", ["type"] = "empty", ["transform"] = new JObject { ["position"] = V() } },
                    new JObject { ["key"] = "box", ["name"] = "Mug box", ["type"] = "primitive", ["primitiveType"] = "cube", ["parentKey"] = "boxes",
                        ["transform"] = new JObject { ["position"] = V(0, 1, 2), ["scale"] = V(.2f, .2f, .2f) } },
                    Label("label", "mug"), Label("other", "notebook")
                }
            });
            var ids = (JObject)built["nodes"];
            string rootId = (string)built["rootId"];
            var position = V(0, 1, 2);
            var box = new JObject { ["x1"] = 10, ["y1"] = 20, ["x2"] = 100, ["y2"] = 120 };
            var args = new JObject {
                ["rootId"] = rootId, ["captureId"] = "cap_selection", ["width"] = 800, ["height"] = 600,
                ["objects"] = new JArray {
                    new JObject { ["label"] = "mug", ["labelId"] = ids["label"].DeepClone(), ["boxId"] = ids["box"].DeepClone(), ["position"] = position.DeepClone(), ["box"] = box.DeepClone() },
                    new JObject { ["label"] = "notebook", ["labelId"] = ids["other"].DeepClone(), ["position"] = position.DeepClone(), ["box"] = box.DeepClone() }
                }
            };
            var bound = Ok("camera_marks_selectable", args);
            Check((string)bound["state"] == "bound" && (int)bound["objects"] == 2, "Marks must bind.");
            var invalid = (JObject)args.DeepClone(); invalid["rootId"] = "go_unknown";
            Check((string)Ok("camera_marks_selectable", invalid)["state"] == "unavailable", "Unknown IDs must be unavailable.");
            await Task.Delay(150);
            var label = Find((string)ids["label"]).GetComponent<ObjectMarkTarget>();
            var target = Find((string)ids["box"]).GetComponent<ObjectMarkTarget>();
            var other = Find((string)ids["other"]).GetComponent<ObjectMarkTarget>();
            Tap(target);
            Check(ObjectMarkSelection.CurrentLabel == "mug" && target.ShellVisible, "Tap must select and highlight the mug.");
            var context = JObject.Parse(ObjectMarkSelection.CurrentContext);
            Check(JToken.DeepEquals(context, new JObject { ["label"] = "mug", ["rootId"] = rootId,
                ["captureId"] = "cap_selection", ["width"] = 800, ["height"] = 600, ["box"] = box, ["position"] = position }), "Context must retain exact capture coordinates.");
            Check(label.GetComponent<ManagedPanel>().IsIntact(), "Selection rig must preserve panel integrity.");
            Tap(target); Check(ObjectMarkSelection.CurrentLabel == null && !target.ShellVisible, "Second tap must clear.");
            Tap(target); Tap(other);
            Check(ObjectMarkSelection.CurrentLabel == "notebook" && !target.ShellVisible && other.ShellVisible, "Another tap must replace selection.");
            Tap(target);
            Check(!label.Ray.gameObject.activeSelf, "Boxed labels must not be pickable in box mode.");
            Find((string)ids["boxes"]).SetActive(false);
            await Task.Delay(100);
            Check(label.Ray.gameObject.activeSelf && label.ShellVisible && ObjectMarkSelection.CurrentLabel == "mug", "Point mode must retain selection and enable the label.");
            Ok("content_save", new JObject { ["slot"] = "selection" });
            Ok("delete_object", new JObject { ["entityId"] = rootId });
            Check(ObjectMarkSelection.CurrentLabel == null, "Deleting the root must clear selection.");
            const string text = "What is this for?";
            Check(ChatHiddenContext.Strip(ChatHiddenContext.Compose(context.ToString(Newtonsoft.Json.Formatting.None), text)) == text, "Hidden context must round-trip.");
            Check(ChatHiddenContext.Strip(text) == text, "Plain text must remain unchanged.");
            return "Object selection checks passed: " + checks + " assertions.";
        }
        finally
        {
            service.Dispose(); Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            var unload = SceneManager.UnloadSceneAsync(scene); while (!unload.isDone) await Task.Delay(10);
        }
    }
}
