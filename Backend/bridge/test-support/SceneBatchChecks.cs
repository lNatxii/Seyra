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

public static class SceneBatchChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("__SceneBatchChecks_" + Guid.NewGuid().ToString("N"));
        var root = new GameObject("__SceneBatchChecks");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/batch-" + Guid.NewGuid().ToString("N")));
        var service = new SceneToolService(root.transform, storage, AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset"));
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        JObject Call(string tool, JObject args) => JObject.Parse(service.Dispatch(new JObject { ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = tool, ["arguments"] = args, ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args) { var response = Call(tool, args); Check((bool)response["success"], response.ToString()); return (JObject)response["data"]; }
        JObject Pose(float x = 0, float scale = 1) => new JObject { ["position"] = new JObject { ["x"] = x, ["y"] = 0, ["z"] = 0 }, ["scale"] = new JObject { ["x"] = scale, ["y"] = scale, ["z"] = scale } };
        JObject Appearance(float red) => new JObject { ["style"] = "unlit", ["color"] = new JObject { ["r"] = red, ["g"] = 0, ["b"] = 0, ["a"] = 1 } };
        JObject Sphere(string key, float x = 0) => new JObject { ["key"] = key, ["name"] = key, ["type"] = "primitive", ["primitiveType"] = "sphere", ["transform"] = Pose(x, .1f), ["appearance"] = Appearance(1) };
        int Total() => (int)Ok("history_list", new JObject())["total"];
        GameObject testHead = null;
        double elapsed = 0;
        try
        {
            var sun = Sphere("sun"); sun["parentKey"] = "group";
            var child = Sphere("child", 2); child["parentKey"] = "sun";
            var planet = Sphere("planet", .5f); planet["parentKey"] = "group";
            var label = new JObject { ["key"] = "label", ["name"] = "Label", ["type"] = "panel", ["transform"] = Pose(),
                ["panel"] = new JObject { ["version"] = 1, ["kind"] = "label", ["title"] = "", ["text"] = "Sun", ["width"] = .2, ["height"] = .06, ["fontSize"] = 24 },
                ["relativeTo"] = new JObject { ["key"] = "sun", ["direction"] = "above", ["gap"] = .01 } };
            var args = new JObject { ["name"] = "Composition", ["nodes"] = new JArray(new JObject { ["key"] = "group", ["name"] = "Group", ["type"] = "empty", ["transform"] = Pose() }, sun, child, planet, label) };
            int before = Total(); var built = Ok("scene_build", args); var ids = (JObject)built["nodes"];
            foreach (var id in ids.Properties()) Ok("scene_get", new JObject { ["entityId"] = id.Value.DeepClone() });
            Check(Total() == before + 1, "Build must record exactly one history entry.");
            var sunBounds = Ok("scene_bounds", new JObject { ["entityId"] = ids["sun"].DeepClone() });
            var labelBounds = Ok("scene_bounds", new JObject { ["entityId"] = ids["label"].DeepClone() });
            Check((float)labelBounds["bounds"]["min"]["y"] > (float)sunBounds["bounds"]["max"]["y"], "Label must be above the sphere subtree.");
            var invalid = (JObject)args.DeepClone(); invalid["nodes"][4]["panel"]["title"] = "Invalid label title";
            int live = root.GetComponentsInChildren<GeneratedObject>(true).Length;
            Check(!(bool)Call("scene_build", invalid)["success"], "Invalid panel must fail.");
            Check(root.GetComponentsInChildren<GeneratedObject>().Length == live && Total() == before + 1, "Failed build must preserve live content and history.");
            Ok("undo", new JObject { ["scope"] = "request" });
            Check((bool)Ok("scene_get", new JObject { ["entityId"] = built["rootId"].DeepClone() })["deleted"], "Request undo must trash the composition.");
            var edited = Ok("scene_build", new JObject { ["name"] = "Edit targets", ["nodes"] = new JArray(Sphere("a"), Sphere("b", .5f)) });
            var edits = new JArray(new[] { "a", "b" }.Select(key => { var appearance = Appearance(.25f); appearance["entityId"] = edited["nodes"][key].DeepClone(); return new JObject { ["tool"] = "set_appearance", ["arguments"] = appearance }; }));
            var invalidEdits = (JArray)edits.DeepClone(); invalidEdits.Add(new JObject { ["tool"] = "rename_object", ["arguments"] = new JObject { ["entityId"] = "missing", ["name"] = "Invalid" } });
            before = Total(); var failure = Call("scene_edit", new JObject { ["operations"] = invalidEdits });
            Check(!(bool)failure["success"] && (int)failure["error"]["index"] == 2, "Edit error must include failing index.");
            Check(Total() == before, "Failed edits must not record history.");
            foreach (string key in new[] { "a", "b" }) Check((float)Ok("scene_get", new JObject { ["entityId"] = edited["nodes"][key].DeepClone() })["appearance"]["color"]["r"] == 1, "Failed edit must restore appearance.");
            Ok("scene_edit", new JObject { ["operations"] = edits });
            foreach (string key in new[] { "a", "b" }) Check((float)Ok("scene_get", new JObject { ["entityId"] = edited["nodes"][key].DeepClone() })["appearance"]["color"]["r"] == .25f, "Valid edit must change both appearances.");
            Ok("undo", new JObject { ["scope"] = "operation" }); Ok("undo", new JObject { ["scope"] = "request" });

            // Measure one synchronous build including parsing, snapshots and response serialization.
            var large = new JObject { ["name"] = "64 nodes", ["nodes"] = new JArray(Enumerable.Range(0, 64).Select(i => Sphere("n" + i, i * .2f))) };
            var watch = System.Diagnostics.Stopwatch.StartNew(); var measured = Call("scene_build", large); watch.Stop(); elapsed = watch.Elapsed.TotalMilliseconds;
            Debug.Log("SceneBatchChecks scene_build 64 nodes: " + elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            Check((bool)measured["success"], measured.ToString());
            Check(elapsed <= 5000, "STOP: scene_build 64 nodes exceeded 5 s: " + elapsed + " ms");
            Ok("undo", new JObject { ["scope"] = "request" });

            // Use an isolated air-only planner and explicit viewer, without device room loading.
            var placement = SpatialPlacementService.ForScene(scene);
            testHead = new GameObject("__BatchAirViewer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(testHead, scene);
            testHead.transform.SetPositionAndRotation(new Vector3(2, 1.6f, 3), Quaternion.Euler(0, 60, 0));
            testHead.SetActive(false);
            var airContext = testHead.AddComponent<RoomContext>();
            airContext.enabled = false;
            airContext.Configure(null, testHead.transform);
            typeof(RoomContext).GetField("simulated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(airContext, true);
            typeof(SpatialPlacementService).GetField("room", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(placement, airContext);
            var placed = Ok("scene_build", new JObject { ["name"] = "Air composition", ["nodes"] = new JArray(Sphere("air")), ["placement"] = new JObject { ["destination"] = "air" } });
            Check((bool)placed["placement"]["applied"], "Air placement must apply.");
            var placedRoot = root.GetComponentsInChildren<GeneratedObject>().First(item => item.EntityId == (string)placed["rootId"]).transform;
            Check(Vector3.Dot(placedRoot.position - testHead.transform.position, testHead.transform.forward) > 0, "Air root must be in front of the viewer.");
            Ok("undo", new JObject { ["scope"] = "request" });
            var airLabel = (JObject)label.DeepClone();
            airLabel["panel"]["height"] = .05;
            var labeled = Ok("scene_build", new JObject { ["name"] = "Labeled air sphere", ["nodes"] = new JArray(Sphere("sun"), airLabel), ["placement"] = new JObject { ["destination"] = "air" } });
            Check((bool)labeled["placement"]["applied"], "A sphere with a 0.05 m label must support air placement.");
            Ok("undo", new JObject { ["scope"] = "request" });
            var tinyLabel = (JObject)airLabel.DeepClone();
            tinyLabel.Remove("relativeTo");
            tinyLabel["panel"]["height"] = .02;
            tinyLabel["panel"]["fontSize"] = 20;
            var rejected = Call("scene_build", new JObject { ["name"] = "Unreadable air label", ["nodes"] = new JArray(tinyLabel), ["placement"] = new JObject { ["destination"] = "air" } });
            string rejectionMessage = (string)rejected["error"]?["message"];
            Check(!(bool)rejected["success"] && (string)rejected["error"]?["code"] == "PLACEMENT_REJECTED" && rejectionMessage != null && rejectionMessage.Contains("Rejected:") && rejectionMessage.Contains("TOO_SMALL_TO_READ"), "An unreadable label must report its placement rejection reasons: " + rejected);
            return "Scene batch basic checks passed: " + checks + "; scene_build 64 nodes: " + elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms";
        }
        catch (Exception error) { Debug.LogError("SceneBatchChecks failed: " + error); throw; }
        finally
        {
            if (testHead != null) Object.DestroyImmediate(testHead);
            Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            var unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone) await Task.Delay(10);
        }
    }
}

