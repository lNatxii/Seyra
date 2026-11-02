using System;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MotionChecks
{
    public static Task<string> Run() => RunCore(true, true);
    public static Task<string> RunPlacementRegression() => RunCore(true, false);
    public static Task<string> RunUndoRegression() => RunCore(false, true);
    static async Task<string> RunCore(bool rotatedPlacement, bool tiltedUndo)
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var scene = SceneManager.CreateScene("__MotionChecks_" + Guid.NewGuid().ToString("N"));
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/motion-" + Guid.NewGuid().ToString("N")));
        var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var system = SeyraMotionSystem.ForScene(scene); system.enabled = false;
        GameObject root = null, head = null;
        SceneToolService service = null;
        int checks = 0, cases = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Motion case " + (cases + 1) + ": " + message); }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Call(string tool, JObject args) => JObject.Parse(service.Dispatch(new JObject {
            ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = tool,
            ["arguments"] = args, ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args = null)
        { var result = Call(tool, args ?? new JObject()); Check((bool)result["success"], result.ToString()); return (JObject)result["data"]; }
        void Error(string tool, JObject args, string code)
        { var result = Call(tool, args); Check(!(bool)result["success"] && (string)result["error"]["code"] == code, result.ToString()); }
        void Fixture()
        {
            service?.Dispose(); if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("__MotionChecks"); SceneManager.MoveGameObjectToScene(root, scene);
            service = new SceneToolService(root.transform, storage, catalog);
        }
        GameObject Find(string id) => root.GetComponentsInChildren<GeneratedObject>(true)
            .First(r => r.EntityId == id && !r.transform.IsChildOf(root.transform.Find("__SeyraTrash"))).gameObject;
        string Create(string name, Vector3 position, string parent = "Generated", bool empty = false)
        {
            var args = new JObject { ["name"] = name, ["parentId"] = parent, ["position"] = V(position.x, position.y, position.z),
                ["rotation"] = V(), ["scale"] = empty ? V(1, 1, 1) : V(.04f, .04f, .04f) };
            if (!empty) args["primitiveType"] = "sphere";
            return (string)Ok(empty ? "create_gameobject" : "create_primitive", args)["entityId"];
        }
        JObject Set(string id, JObject motion) => Ok("motion_set", new JObject { ["entityId"] = id, ["motion"] = motion });
        JObject Orbit(string pivot, float speed = 90) => new JObject { ["kind"] = "orbit", ["pivotId"] = pivot, ["speed"] = speed };
        JObject Spin(float speed = 90) => new JObject { ["kind"] = "spin", ["speed"] = speed };
        JObject Bob() => new JObject { ["kind"] = "bob", ["amplitude"] = .02, ["period"] = 2 };
        JObject Get(string id) => Ok("scene_get", new JObject { ["entityId"] = id });
        void Control(string id, string action) => Ok("motion_control", new JObject { ["entityId"] = id, ["action"] = action });
        try
        {
            Fixture();
            // 1. Spin integrates a simulated second from its base, not the previous frame's pose.
            string body = Create("Spin", Vector3.zero); Set(body, Spin()); system.Step(1);
            Check(Mathf.Abs(Quaternion.Angle(Quaternion.identity, Find(body).transform.localRotation) - 90) <= 1, "Spin must turn 90 degrees in one second."); cases++;

            // 2. A moving pivot preserves the orbit radius.
            Fixture();
            string group = Create("Model", Vector3.zero, empty: true);
            string sun = Create("Sun", Vector3.zero, group), earth = Create("Earth", new Vector3(.3f, 0, 0), group);
            Set(earth, Orbit(sun));
            Ok("set_transform", new JObject { ["entityId"] = sun, ["space"] = "local", ["position"] = V(.05f, .02f) });
            system.Step(.5f);
            Check(Mathf.Abs(Vector3.Distance(Find(earth).transform.position, Find(sun).transform.position) - .3f) < .0001f, "Moving pivot changed radius."); cases++;

            // 3. Moon depends on the planet in the same Step, including a changed pivot pose.
            string moon = Create("Moon", Find(earth).transform.localPosition + new Vector3(.08f, 0, 0), group);
            Set(moon, Orbit(earth, 180)); system.Step(.25f);
            Check(Mathf.Abs(Vector3.Distance(Find(moon).transform.position, Find(earth).transform.position) - .08f) < .0001f, "Moon lagged behind its orbiting pivot."); cases++;

            // 4. Parent transforms carry the entire model and scale its orbital distances.
            Ok("set_transform", new JObject { ["entityId"] = group, ["space"] = "local", ["position"] = V(1, .5f, 2), ["rotation"] = V(0, 37), ["scale"] = V(2, 2, 2) });
            system.Step(.25f);
            Check(Mathf.Abs(Vector3.Distance(Find(earth).transform.position, Find(sun).transform.position) - .6f) < .0001f, "Root transform lost proportional orbit."); cases++;

            // 5. Channel ownership, pause, invalid targets and dependency cycles.
            Error("motion_set", new JObject { ["entityId"] = earth, ["motion"] = Bob() }, "MOTION_CHANNEL_BUSY");
            Control(earth, "pause");
            Error("set_transform", new JObject { ["entityId"] = earth, ["space"] = "local", ["position"] = V() }, "MOTION_DRIVEN");
            Error("motion_set", new JObject { ["entityId"] = "Generated", ["motion"] = Spin() }, "MOTION_TARGET_INVALID");
            Error("motion_set", new JObject { ["entityId"] = sun, ["motion"] = Orbit(earth) }, "MOTION_PIVOT_INVALID"); cases++;

            // 6. Pause/resume preserve phase; reset returns to the captured base.
            Fixture(); body = Create("Controlled", Vector3.zero); Set(body, Spin()); system.Step(.25f);
            var before = Find(body).transform.localRotation; Control(body, "pause"); system.Step(1);
            Check(Quaternion.Angle(before, Find(body).transform.localRotation) < .001f, "Pause advanced spin.");
            Control(body, "resume"); system.Step(.25f);
            Check(Mathf.Abs(Quaternion.Angle(Quaternion.identity, Find(body).transform.localRotation) - 45) < 1, "Resume lost phase.");
            Control(body, "reset"); Check(Quaternion.Angle(Quaternion.identity, Find(body).transform.localRotation) < .001f, "Reset lost base."); cases++;

            // 7. Undo motion restores its pose; unrelated undo must not overwrite driven channels.
            Fixture(); body = Create("Undo", Vector3.zero); var configured = Set(body, Spin()); system.Step(.5f);
            Ok("undo", new JObject { ["scope"] = "operation", ["expectedOperationId"] = configured["operationId"] });
            Check(Find(body).GetComponent<SeyraMotion>() == null && Quaternion.Angle(Find(body).transform.localRotation, Quaternion.identity) < .001f, "Undo motion did not restore pose and configuration.");
            Set(body, Spin()); Set(body, Bob()); string unrelated = Create("Unrelated", new Vector3(2, 0, 0));
            Ok("rename_object", new JObject { ["entityId"] = unrelated, ["name"] = "Renamed" }); system.Step(.5f);
            before = Find(body).transform.localRotation;
            var beforePosition = Find(body).transform.localPosition;
            Ok("undo", new JObject { ["scope"] = "operation" });
            Check(Quaternion.Angle(before, Find(body).transform.localRotation) < .001f, "Unrelated undo rewound a driven rotation.");
            Check(Vector3.Distance(beforePosition, Find(body).transform.localPosition) < .0001f, "Unrelated undo rewound a driven position.");
            // A failed batch must roll back even a same-signature motion_set that rebased the object.
            var captured = Find(body).GetComponent<SeyraMotion>().Capture().ToJson();
            Error("scene_edit", new JObject { ["operations"] = new JArray(
                new JObject { ["tool"] = "motion_set", ["arguments"] = new JObject { ["entityId"] = body, ["motion"] = Spin() } },
                new JObject { ["tool"] = "motion_remove", ["arguments"] = new JObject { ["entityId"] = body, ["kind"] = "orbit" } }) }, "MOTION_NOT_FOUND");
            Check(JToken.DeepEquals(captured, Find(body).GetComponent<SeyraMotion>().Capture().ToJson()), "Failed batch lost base, phase or pause.");
            if (tiltedUndo)
            {
                Fixture(); body = Create("Tilted undo", Vector3.zero);
                Set(body, new JObject { ["kind"] = "spin", ["axis"] = V(.41f, .91f), ["speed"] = 90 });
                system.Step(.2f);
                var tiltedSignature = Find(body).GetComponent<SeyraMotion>().Signature;
                Ok("motion_remove", new JObject { ["entityId"] = body, ["kind"] = "spin" });
                Ok("undo", new JObject { ["scope"] = "operation" });
                bool sameTiltedSignature = Find(body).GetComponent<SeyraMotion>().Signature == tiltedSignature;
                Ok("undo", new JObject { ["scope"] = "operation" });
                Check(sameTiltedSignature, "Undo changed the exact tilted-axis signature.");
                Check(Find(body).GetComponent<SeyraMotion>() == null, "Chained undo with a tilted axis must remove the original motion without HISTORY_CONFLICT.");
            }
            cases++;

            // 8. Trash freezes phase; restoring motion and an absent pivot resumes it.
            Fixture(); sun = Create("Pivot", Vector3.zero); earth = Create("Orbit", new Vector3(.3f, 0, 0)); Set(earth, Orbit(sun)); system.Step(.25f);
            captured = Find(earth).GetComponent<SeyraMotion>().Capture().ToJson();
            Ok("delete_object", new JObject { ["entityId"] = earth }); system.Step(.5f);
            Ok("restore_object", new JObject { ["entityId"] = earth });
            Check(JToken.DeepEquals(captured, Find(earth).GetComponent<SeyraMotion>().Capture().ToJson()), "Trash advanced or reset phase.");
            Ok("delete_object", new JObject { ["entityId"] = sun }); system.Step(.5f);
            Check((string)Get(earth)["motions"]["status"] == "pivot_missing", "Trashed pivot did not freeze motion.");
            Ok("restore_object", new JObject { ["entityId"] = sun }); system.Step(.25f);
            Check((string)Get(earth)["motions"]["status"] == "running", "Restored pivot did not resume."); cases++;

            // 9. Build all node types atomically; placement must reserve more than the current pose.
            Fixture(); var placement = SpatialPlacementService.ForScene(scene);
            head = new GameObject("__MotionViewer"); SceneManager.MoveGameObjectToScene(head, scene); head.transform.position = new Vector3(2, 1.6f, 3);
            if (rotatedPlacement) head.transform.rotation = Quaternion.Euler(0, 37, 0);
            head.SetActive(false); var room = head.AddComponent<RoomContext>(); room.enabled = false; room.Configure(null, head.transform);
            typeof(RoomContext).GetField("simulated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(room, true);
            typeof(SpatialPlacementService).GetField("room", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(placement, room);
            JObject Transform(float x = 0, float scale = 1) => new JObject { ["position"] = V(x), ["scale"] = V(scale, scale, scale) };
            var built = Ok("scene_build", new JObject { ["name"] = "Animated model", ["nodes"] = new JArray(
                new JObject { ["key"] = "sun", ["name"] = "Sun", ["type"] = "primitive", ["primitiveType"] = "sphere", ["transform"] = Transform(0, .04f) },
                new JObject { ["key"] = "carrier", ["name"] = "Carrier", ["type"] = "empty", ["transform"] = Transform(.22f), ["motions"] = new JArray(new JObject { ["kind"] = "orbit", ["pivotKey"] = "sun", ["speed"] = 30 }) },
                new JObject { ["key"] = "body", ["name"] = "Body", ["type"] = "primitive", ["primitiveType"] = "sphere", ["parentKey"] = "carrier", ["transform"] = Transform(0, .04f), ["motions"] = new JArray(Spin(30)) },
                new JObject { ["key"] = "label", ["name"] = "Label", ["type"] = "panel", ["parentKey"] = "carrier", ["transform"] = Transform(), ["panel"] = new JObject { ["version"] = 1, ["kind"] = "label", ["title"] = "", ["text"] = "Planet", ["width"] = .16, ["height"] = .06, ["fontSize"] = 24 }, ["relativeTo"] = new JObject { ["key"] = "body", ["direction"] = "above", ["gap"] = .06 } },
                new JObject { ["key"] = "point", ["name"] = "Point", ["type"] = "point", ["targetKey"] = "body" },
                new JObject { ["key"] = "line", ["name"] = "Line", ["type"] = "line", ["parentKey"] = "carrier", ["line"] = new JObject { ["points"] = new JArray(new JObject { ["key"] = "point" }, new JObject { ["key"] = "label" }), ["width"] = .003 } }),
                ["placement"] = new JObject { ["destination"] = "air" } });
            group = (string)built["rootId"]; var groupTransform = Find(group).transform;
            if (rotatedPlacement) Check(Mathf.Abs(Mathf.DeltaAngle(groupTransform.eulerAngles.y, 37)) < .01f, "Regression requires a 37-degree placement root rotation.");
            var points = (List<Vector3>)typeof(SceneToolService).GetMethod("GeometryPoints", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, new object[] { Find(group), false });
            var instant = SpatialGeometry.Project(points.Select(p => Quaternion.Inverse(groupTransform.rotation) * (p - groupTransform.position)), Quaternion.identity);
            Check(placement.Placement(groupTransform).Box.Size.x > instant.size.x + .1f, "Placement used only the instantaneous geometry instead of the orbit sweep."); cases++;

            // 10. Save/load retains base, phase and pause, with a missing pivot and legacy v6.
            earth = (string)built["nodes"]["carrier"]; body = (string)built["nodes"]["body"]; sun = (string)built["nodes"]["sun"];
            system.Step(.7f); Control(group, "pause");
            captured = Find(earth).GetComponent<SeyraMotion>().Capture().ToJson();
            var spinState = Find(body).GetComponent<SeyraMotion>().Capture().ToJson();
            Ok("content_save", new JObject { ["slot"] = "bound" });
            Ok("content_load", new JObject { ["slot"] = "bound" });
            Check(Find(earth).GetComponent<SeyraMotion>().Capture().Pivot == Find(sun).transform, "Loaded pivot must bind to the loaded object, not the previous instance in trash.");
            Check(JToken.DeepEquals(captured, Find(earth).GetComponent<SeyraMotion>().Capture().ToJson()), "Loaded bound orbit lost base, phase or pause.");
            Ok("delete_object", new JObject { ["entityId"] = sun });
            var saved = Ok("content_save", new JObject { ["slot"] = "motion" }); Check((int)saved["version"] == 8, "Expected v8.");
            var document = JObject.Parse(File.ReadAllText(Path.Combine(storage, "motion.json")));
            Check(document["root"]["motion"].Type == JTokenType.Null && ((JArray)document["nodes"]).All(n => n["motion"] != null), "Every v8 node must carry motion.");
            Ok("content_load", new JObject { ["slot"] = "motion" }); system.Step(0);
            Check(JToken.DeepEquals(captured, Find(earth).GetComponent<SeyraMotion>().Capture().ToJson()), "Loaded orbit lost base, phase or pause.");
            Check(JToken.DeepEquals(spinState, Find(body).GetComponent<SeyraMotion>().Capture().ToJson()), "Loaded spin lost base, phase or pause.");
            Check((string)Get(earth)["motions"]["status"] == "pivot_missing", "Missing saved pivot must load frozen.");
            ((JObject)document["root"]).Remove("interaction"); foreach (JObject node in (JArray)document["nodes"]) node.Remove("interaction");
            document["version"] = 6; ((JObject)document["root"]).Remove("motion"); foreach (var node in (JArray)document["nodes"]) ((JObject)node).Remove("motion");
            File.WriteAllText(Path.Combine(storage, "legacy.json"), document.ToString());
            Ok("content_load", new JObject { ["slot"] = "legacy" });
            Check(!root.GetComponentsInChildren<SeyraMotion>().Any(), "Legacy v6 should load without motion."); cases++;

            // Float contract boundaries must survive copying, saving and strict JSON loading.
            Fixture(); body = Create("Minimum bob", Vector3.zero);
            Set(body, new JObject { ["kind"] = "bob", ["amplitude"] = .005, ["period"] = .2 });
            system.Step(.05f); Control(body, "pause");
            captured = Find(body).GetComponent<SeyraMotion>().Capture().ToJson();
            Ok("content_save", new JObject { ["slot"] = "minimum" });
            Ok("content_load", new JObject { ["slot"] = "minimum" });
            Check(Find(body).GetComponent<SeyraMotion>().Capture().Position.Amplitude == .005f, "Loaded minimum amplitude must remain exactly 0.005f.");
            Check(JToken.DeepEquals(captured, Find(body).GetComponent<SeyraMotion>().Capture().ToJson()), "Minimum bob roundtrip lost base, phase or pause.");
            var tolerantRotation = new MotionState { Spin = new SpinSpec { Speed = 90 }, BaseRotation = new Quaternion(0, 0, 0, 1.0001f) };
            var parsedRotation = MotionState.Parse(tolerantRotation.ToJson());
            Check(parsedRotation.BaseRotation.w == tolerantRotation.BaseRotation.w, "Base rotation within the 0.001 unit tolerance must load without clipping its components.");

            // Requested dynamic uncertainty check: motion after a direct spatial_place.
            Fixture(); body = Create("Direct placement", Vector3.zero);
            var placed = Ok("spatial_place", new JObject { ["entityId"] = body, ["destination"] = "air" });
            Check((bool)placed["applied"], "Direct spatial_place failed.");
            Check(!(bool)Get(body)["spatial"]["needsPlacement"], "Initial direct placement should be valid.");
            var initialPosition = Find(body).transform.position; Set(body, Bob()); system.Step(.25f);
            Check(Vector3.Distance(initialPosition, Find(body).transform.position) > .005f, "Bob did not move the placed object.");
            Check(!(bool)Get(body)["spatial"]["needsPlacement"], "Unobstructed bob falsely invalidated direct placement.");
            return "Motion checks passed: " + checks + " assertions; " + cases + " design cases; same-signature rollback and dynamic NeedsPlacement bob passed.";
        }
        finally
        {
            service?.Dispose(); if (root != null) Object.DestroyImmediate(root); if (head != null) Object.DestroyImmediate(head);
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            var unload = SceneManager.UnloadSceneAsync(scene); while (!unload.isDone) await Task.Delay(10);
        }
    }
}
