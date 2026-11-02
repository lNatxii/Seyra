using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Seyra.SceneTools;
using Seyra.Spatial;
using Seyra.UI;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class InteractionChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var scene = SceneManager.CreateScene("__InteractionChecks_" + Guid.NewGuid().ToString("N"));
        var root = new GameObject("__InteractionChecks"); SceneManager.MoveGameObjectToScene(root, scene);
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/interaction-" + Guid.NewGuid().ToString("N")));
        var library = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var catalog = Resources.Load<UIPresentationCatalog>("SeyraUIPresentations");
        var service = new SceneToolService(root.transform, storage, library, catalog);
        var motionSystem = SeyraMotionSystem.ForScene(scene); motionSystem.enabled = false;
        int checks = 0, cases = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Interaction case " + (cases + 1) + ": " + message); }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Call(string tool, JObject args = null) => JObject.Parse(service.Dispatch(new JObject {
            ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = tool,
            ["arguments"] = args ?? new JObject(), ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args = null) { var result = Call(tool, args); Check((bool)result["success"], result.ToString()); return (JObject)result["data"]; }
        void Error(string tool, JObject args, string code) { var result = Call(tool, args); Check(!(bool)result["success"] && (string)result["error"]?["code"] == code, result.ToString()); }
        JObject Id(string id) => new JObject { ["entityId"] = id };
        GameObject Find(string id) => root.GetComponentsInChildren<GeneratedObject>(true).First(g => g.EntityId == id && !g.transform.IsChildOf(root.transform.Find("__SeyraTrash"))).gameObject;
        JObject Get(string id) => Ok("scene_get", Id(id));
        void Undo() => Ok("undo", new JObject { ["scope"] = "operation" });
        string Create(string name, float x, string parent = "Generated", bool empty = false)
        {
            var args = new JObject { ["name"] = name, ["parentId"] = parent, ["position"] = V(x, 1, 2), ["rotation"] = V(), ["scale"] = V(1, 1, 1) };
            if (!empty) { args["primitiveType"] = "sphere"; args["scale"] = V(.1f, .1f, .1f); }
            return (string)Ok(empty ? "create_gameobject" : "create_primitive", args)["entityId"];
        }
        JObject Panel(string title) => new JObject { ["version"] = 1, ["kind"] = "panel", ["title"] = title, ["text"] = "Facts about " + title + ".", ["width"] = .4, ["height"] = .3, ["fontSize"] = 24 };
        JObject Set(string id, bool grabbable = false) => Ok("interaction_set", new JObject { ["entityId"] = id, ["interactive"] = true, ["grabbable"] = grabbable, ["detail"] = new JObject { ["panel"] = Panel(id) } });
        void Send(SeyraInteractive interactive, PointerEventType type, Vector3 point) => interactive.Ray.PublishPointerEvent(new PointerEvent(1001, type, new Pose(point, Quaternion.identity)));
        void Tap(SeyraInteractive interactive) { var point = interactive.transform.position; Send(interactive, PointerEventType.Hover, point); Send(interactive, PointerEventType.Select, point); Send(interactive, PointerEventType.Unselect, point); Send(interactive, PointerEventType.Unhover, point); }
        try
        {
            Check(SeyraLazyFollow.ResolveHead() != null, "The host scene must provide a viewer.");
            string first = Create("First", -.25f), second = Create("Second", .25f);
            var configured = Set(first); Set(second);
            var one = Find(first).GetComponent<SeyraInteractive>(); var two = Find(second).GetComponent<SeyraInteractive>();
            string detailId = (string)configured["interaction"]["detailId"];
            await Task.Delay(150);
            Tap(one); Check(one.Detail.WindowVisible, "Tap must open the detail.");
            Tap(two); Check(two.Detail.WindowVisible && !one.Detail.WindowVisible, "Exclusive selection must close the previous detail.");
            Tap(two); Check(!two.Detail.WindowVisible, "Tap must close the detail."); cases++;

            Send(one, PointerEventType.Hover, one.transform.position);
            Check(one.GetComponentInChildren<SeyraInteractionRig>().transform.Find("Highlight").gameObject.activeSelf, "Hover must enable highlight.");
            Check((bool)Get(first)["appearanceIntact"] && !Find(first).GetComponent<MeshRenderer>().HasPropertyBlock(), "Highlight must preserve appearance integrity.");
            Send(one, PointerEventType.Unhover, one.transform.position);
            Check(!one.GetComponentInChildren<SeyraInteractionRig>().transform.Find("Highlight").gameObject.activeSelf, "Unhover must disable highlight."); cases++;

            string pivot = Create("Pivot", 0);
            Ok("motion_set", new JObject { ["entityId"] = first, ["motion"] = new JObject { ["kind"] = "orbit", ["pivotId"] = pivot, ["speed"] = 30 } });
            Tap(one); var originalSize = one.Detail.transform.lossyScale;
            motionSystem.Step(.5f); one.Detail.GetComponent<SeyraFollowTarget>().Snap(); var beforeFollow = one.Detail.transform.position;
            motionSystem.Step(.5f); one.Detail.GetComponent<SeyraFollowTarget>().Snap();
            Check(Vector3.Distance(beforeFollow, one.Detail.transform.position) > .001f, "Detail must follow the orbit.");
            string carrier = Create("Carrier", 0, empty: true);
            Ok("motion_remove", new JObject { ["entityId"] = first, ["kind"] = "orbit" });
            Ok("set_parent", new JObject { ["entityId"] = first, ["parentId"] = carrier, ["worldPositionStays"] = true });
            Ok("set_transform", new JObject { ["entityId"] = carrier, ["space"] = "local", ["scale"] = V(2, 2, 2) });
            Check(one.Detail.transform.lossyScale.Equals(originalSize), "Detail scale must not inherit target ancestry."); cases++;

            Set(first, true); one = Find(first).GetComponent<SeyraInteractive>(); await Task.Delay(150);
            var position = one.transform.localPosition; var rotation = one.transform.localRotation; var scale = one.transform.localScale;
            var start = one.transform.position; Send(one, PointerEventType.Hover, start); Send(one, PointerEventType.Select, start); Send(one, PointerEventType.Move, start + Vector3.right * .2f);
            Check(one.IsHeld && Vector3.Distance(one.transform.position, start) > .1f, "SDK grabbing must move the target: held=" + one.IsHeld + ", distance=" + Vector3.Distance(one.transform.position, start) + ", element=" + one.Ray.PointableElement + ", grabTarget=" + one.GetComponentInChildren<Grabbable>()?.Transform);
            Send(one, PointerEventType.Unselect, start + Vector3.right * .2f); await Task.Delay(550);
            Check(!one.IsHeld && position.Equals(one.transform.localPosition) && rotation.Equals(one.transform.localRotation) && scale.Equals(one.transform.localScale), "Return must restore exact pose and scale.");
            Undo(); Check(Find(first).GetComponent<SeyraInteractive>() != null, "Undo after return must not conflict."); cases++;

            Set(first, true); one = Find(first).GetComponent<SeyraInteractive>(); await Task.Delay(150);
            Ok("motion_set", new JObject { ["entityId"] = carrier, ["motion"] = new JObject { ["kind"] = "spin", ["speed"] = 30 } });
            start = one.transform.position; Send(one, PointerEventType.Hover, start); Send(one, PointerEventType.Select, start); motionSystem.Step(.1f);
            Check(Find(carrier).GetComponent<SeyraMotion>().Status == "held", "Grabbing a child must hold carrier motion.");
            Send(one, PointerEventType.Cancel, start); await Task.Delay(550); motionSystem.Step(.1f);
            Check(Find(carrier).GetComponent<SeyraMotion>().Status == "running", "Carrier motion must resume after return."); cases++;

            detailId = one.Specification.DetailId;
            Error("set_transform", new JObject { ["entityId"] = detailId, ["space"] = "world", ["position"] = V() }, "MOTION_DRIVEN");
            Error("delete_object", Id(detailId), "INTERACTION_DETAIL_OWNED");
            Error("set_parent", new JObject { ["entityId"] = detailId, ["parentId"] = "Generated", ["worldPositionStays"] = true }, "INTERACTION_DETAIL_OWNED"); cases++;

            Ok("delete_object", Id(carrier)); Check((bool)Get(detailId)["deleted"], "Deleting an ancestor must trash the detail.");
            Ok("restore_object", Id(carrier)); Check(!(bool)Get(detailId)["deleted"], "Restoring an ancestor must restore the detail.");
            Undo(); Check((bool)Get(detailId)["deleted"], "Undo restoration must trash the detail together."); Undo();
            Check(!(bool)Get(detailId)["deleted"], "Undo deletion must restore both entities.");
            Ok("content_save", new JObject { ["slot"] = "interactive" });
            var document = JObject.Parse(File.ReadAllText(Path.Combine(storage, "interactive.json")));
            Check((int)document["version"] == 8 && document["root"]["interaction"].Type == JTokenType.Null && ((JArray)document["nodes"]).All(n => n["interaction"] != null), "v8 requires interaction on every node.");
            Ok("content_load", new JObject { ["slot"] = "interactive" });
            one = Find(first).GetComponent<SeyraInteractive>();
            Check(one != null && one.Specification.Grabbable && one.Detail.GetComponent<SeyraFollowTarget>().Target == one.transform && !one.Detail.Window.GrabFrame.gameObject.activeSelf, "Load must reconstruct interaction and detail ownership.");
            await Task.Delay(150); Tap(one); bool visible = one.Detail.WindowVisible;
            Ok("content_save", new JObject { ["slot"] = "visible" }); Ok("content_load", new JObject { ["slot"] = "visible" });
            Check(Find(first).GetComponent<SeyraInteractive>().Detail.WindowVisible == visible, "Detail visibility must persist."); cases++;

            document["version"] = 7; ((JObject)document["root"]).Remove("interaction");
            foreach (JObject node in (JArray)document["nodes"]) node.Remove("interaction");
            File.WriteAllText(Path.Combine(storage, "legacy.json"), document.ToString());
            Ok("content_load", new JObject { ["slot"] = "legacy" });
            Check(root.GetComponentsInChildren<SeyraInteractive>().Length == 0, "Legacy v7 must load without interaction."); cases++;
            return "Interaction checks passed: " + checks + " assertions; " + cases + " design cases.";
        }
        finally
        {
            service.Dispose(); Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            var unload = SceneManager.UnloadSceneAsync(scene); while (!unload.isDone) await Task.Delay(10);
        }
    }
}
