using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using Seyra.Voice;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PanelChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run panel checks in Play Mode.");
        var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var root = new GameObject("__PanelChecks");
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/panels-" + Guid.NewGuid().ToString("N")));
        var service = new SceneToolService(root.transform, storage, catalog);
        var voiceHost = new GameObject("__PanelChecksVoice");
        var voice = voiceHost.AddComponent<SpokenReplies>();
        voice.SimulateOutput = true;
        int checks = 0;
        void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        JObject Call(string tool, JObject args = null, string requestId = null) => JObject.Parse(service.Dispatch(new JObject {
            ["version"] = 1, ["type"] = "tool_request", ["requestId"] = requestId ?? Guid.NewGuid().ToString(), ["tool"] = tool,
            ["arguments"] = args ?? new JObject(), ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args = null, string requestId = null)
        { var result = Call(tool, args, requestId); Require((bool)result["success"], result.ToString()); return (JObject)result["data"]; }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Data(string kind = "panel", string text = "  <b>Literal</b>\nSegunda línea  ") => new JObject {
            ["version"] = 1, ["kind"] = kind, ["title"] = kind == "panel" ? "Panel de prueba" : "", ["text"] = text,
            ["width"] = .8, ["height"] = kind == "panel" ? .6 : .12, ["fontSize"] = 24d };
        JObject CreateArgs(string name, JObject panel) => new JObject { ["name"] = name, ["parentId"] = "Generated",
            ["position"] = V(0, 1, 2), ["rotation"] = V(), ["scale"] = V(1, 1, 1), ["panel"] = panel };
        JObject Get(string id) => Ok("scene_get", new JObject { ["entityId"] = id });
        void Undo(JObject operation) => Ok("undo", new JObject { ["scope"] = "operation", ["expectedOperationId"] = operation["operationId"] });
        GameObject Owned(string id) => root.GetComponentsInChildren<GeneratedObject>(true).First(g => g.EntityId == id && !g.transform.IsChildOf(root.transform.Find("__SeyraTrash"))).gameObject;
        try
        {
            var group = Ok("request_begin", new JObject { ["name"] = "PanelRequest" });
            var requestId = Guid.NewGuid().ToString(); var original = Data(); var args = CreateArgs("ManagedPanel", original);
            var created = Ok("create_panel", args, requestId); var id = (string)created["entityId"];
            Require(JToken.DeepEquals(created, Ok("create_panel", args, requestId)), "Panel creation was not deduplicated.");
            Require((string)created["parentId"] == (string)group["entityId"] && (string)created["requestGroupId"] == (string)group["entityId"], "Panel lost request provenance.");
            var panel = Owned(id).GetComponent<ManagedPanel>();
            await Task.Delay(150);
            var signature=JObject.Parse(panel.StateSignature);
            var baseline=JArray.Parse((string)typeof(ManagedPanel).GetField("structure",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(panel));
            var actual=JArray.Parse((string)signature["structure"]);
            signature["structure"]=new JArray(actual.Where(n=>!baseline.Any(b=>JToken.DeepEquals(n,b))));
            signature["newComponents"]=new JArray(panel.GetComponentsInChildren<Transform>(true).SelectMany(t=>t.GetComponents<Component>())
                .Where(c=>!baseline.SelectMany(b=>(JArray)b["components"]).Any(v=>(int)v==c.GetInstanceID())).Select(c=>c.GetType().FullName));
            Require((bool)Get(id)["panelIntact"], "Panel changed during its first frames: "+signature);
            Require(panel.Window.GrabFrame.Grabbable.Transform == panel.transform, "Native grab targets the visual child rather than the logical entity.");
            Require(!panel.GetComponentsInChildren<TMP_Text>(true).First(t => t.text == (string)original["text"]).richText, "Panel markup was interpreted.");
            var updatedData = Data(text: new string('x', 1200) + "\nTexto final"); updatedData["width"] = 1.1; updatedData["height"] = .7; updatedData["fontSize"] = 32d;
            var updated = Ok("update_panel", new JObject { ["entityId"] = id, ["panel"] = updatedData });
            Require(JToken.DeepEquals(updatedData, updated["panel"]), "Full panel replacement lost data.");
            Require(JToken.DeepEquals(created["transform"], updated["transform"]), "Content editing changed pose.");
            Undo(updated); Require(JToken.DeepEquals(original, Get(id)["panel"]), "Undo did not restore panel content.");
            var longText = Data(text: string.Join("\n", Enumerable.Range(1, 40).Select(line => "Scroll indicator line " + line)));
            var longUpdate = Ok("update_panel", new JObject { ["entityId"] = id, ["panel"] = longText });
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Surface); Canvas.ForceUpdateCanvases();
            var panelScroll = panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            var indicator = panelScroll.GetComponent<UIScrollIndicator>();
            var track = panelScroll.viewport.Find("ScrollIndicatorTrack");
            Require(indicator != null && track != null && track.GetComponent<CanvasRenderer>().GetAlpha() > 0.5f, "Long panel text must show a scroll indicator track.");
            Undo(longUpdate);
            var shortUpdate = Ok("update_panel", new JObject { ["entityId"] = id, ["panel"] = Data(text: "One line.") });
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Surface); Canvas.ForceUpdateCanvases();
            Require(panelScroll.GetComponent<UIScrollIndicator>() != null && track.GetComponent<CanvasRenderer>().GetAlpha() < 0.5f, "Single-line panel text must hide the scroll indicator.");
            Undo(shortUpdate);
            var label = Ok("create_panel", CreateArgs("ManagedLabel", Data("label"))); var labelId = (string)label["entityId"];
            Require(Owned(labelId).GetComponent<ManagedPanel>().Window == null, "A label created a window.");
            var primitive = Ok("create_primitive", new JObject { ["name"] = "Reference", ["parentId"] = "Generated", ["primitiveType"] = "cube",
                ["position"] = V(2, 1, 2), ["rotation"] = V(10, 20, 30), ["scale"] = V(1, 2, 1) });
            foreach (var direction in new[] { "above", "below", "left", "right", "front", "behind" })
            {
                var placed = Ok("place_relative", new JObject { ["entityId"] = id, ["referenceId"] = primitive["entityId"], ["direction"] = direction, ["gap"] = .15 });
                Require(placed["bounds"] != null, "Panel placement did not return bounds."); Undo(placed);
            }
            Ok("request_end", new JObject { ["requestGroupId"] = group["entityId"] });
            var saved = Ok("content_save", new JObject { ["slot"] = "mixed" });
            Require((int)saved["version"] == 8, "Expected persistence v8.");
            var document = JObject.Parse(File.ReadAllText(Path.Combine(storage, "mixed.json")));
            Require((int)saved["nodeCount"] == 4 && ((JArray)document["nodes"]).Count == 4, "Internal UI objects leaked into the saved graph.");
            Require(document["nodes"].OfType<JObject>().Count(n => (string)n["kind"] == "panel") == 2, "Mixed content lost its panels.");
            var deleted = Ok("delete_object", new JObject { ["entityId"] = id });
            Require((bool)deleted["deleted"], "Panel was not put in trash.");
            Require(!(bool)Call("update_panel", new JObject { ["entityId"] = id, ["panel"] = original })["success"], "A deleted panel was editable.");
            var restored = Ok("restore_object", new JObject { ["entityId"] = id });
            Require(JToken.DeepEquals(created["panel"], restored["panel"]), "Restoring lost panel content.");
            Undo(restored); Undo(deleted);
            var beforeLoad = Get(id); var oldComponent = beforeLoad["components"].OfType<JObject>().First(c => (string)c["type"] == typeof(ManagedPanel).FullName)["componentId"].ToString();
            var loaded = Ok("content_load", new JObject { ["slot"] = "mixed" });
            await Task.Delay(150);
            var afterLoad = Get(id);
            Require((bool)afterLoad["panelIntact"] && JToken.DeepEquals(original, afterLoad["panel"]), "Loading did not reconstruct the panel.");
            Require(afterLoad["components"].OfType<JObject>().First(c => (string)c["type"] == typeof(ManagedPanel).FullName)["componentId"].ToString() != oldComponent, "Component identity was incorrectly persisted.");
            Undo(loaded); Require(Owned(id).GetComponent<ManagedPanel>() == panel, "Load undo did not restore the original panel instance.");
            var inner = panel.GetComponentsInChildren<TMP_Text>(true).First(t => t.text == (string)original["text"]);
            var internalNode = Ok("scene_find", new JObject { ["rootId"] = id, ["componentType"] = "TextMeshProUGUI", ["limit"] = 100 })["nodes"].First();
            var internalId = internalNode["entityId"];
            foreach (var operation in new[] { "set_transform", "rename_object", "delete_object" })
            {
                var mutation = new JObject { ["entityId"] = internalId };
                if (operation == "set_transform") { mutation["space"] = "local"; mutation["position"] = V(); }
                if (operation == "rename_object") mutation["name"] = "Forbidden";
                Require(!(bool)Call(operation, mutation)["success"], "Internal mutation was accepted: " + operation);
            }
            var parented = CreateArgs("ForbiddenChild", original); parented["parentId"] = id;
            Require(!(bool)Call("create_panel", parented)["success"], "A panel was accepted as a content parent.");
            var latest = Ok("rename_object", new JObject { ["entityId"] = id, ["name"] = "RenamedPanel" });
            var bytes = File.ReadAllText(Path.Combine(storage, "mixed.json")); inner.text = "External edit";
            Require(!(bool)Call("content_save", new JObject { ["slot"] = "mixed" })["success"], "External visual edits were silently saved.");
            Require(bytes == File.ReadAllText(Path.Combine(storage, "mixed.json")), "Rejected save overwrote the slot.");
            Require(!(bool)Call("update_panel", new JObject { ["entityId"] = id, ["panel"] = updatedData })["success"], "External visual edit was overwritten.");
            var conflict = Call("undo", new JObject { ["scope"] = "operation", ["expectedOperationId"] = latest["operationId"] });
            Require(!(bool)conflict["success"] && ((string)conflict["error"]["message"]).Contains("HISTORY_CONFLICT"), "External changes did not block undo.");
            inner.text = (string)original["text"]; Undo(latest);
            foreach (var changes in new[] { new JObject { ["height"] = .1 }, new JObject { ["fontSize"] = 100 }, new JObject { ["unknown"] = true }, new JObject { ["version"] = 2 } })
            {
                var bad = (JObject)original.DeepClone(); foreach (var property in changes.Properties()) bad[property.Name] = property.Value.DeepClone();
                Require(!(bool)Call("update_panel", new JObject { ["entityId"] = id, ["panel"] = bad })["success"], "Invalid panel content was accepted.");
                Require(JToken.DeepEquals(original, Get(id)["panel"]), "Rejected update changed panel content.");
            }
            foreach (var invalid in new[] { "future", "badPanel", "panelParent" })
            {
                var bad = (JObject)document.DeepClone();
                if (invalid == "future") bad["version"] = 9;
                if (invalid == "badPanel") bad["nodes"].OfType<JObject>().First(n => (string)n["kind"] == "panel")["panel"]["version"] = 99;
                if (invalid == "panelParent") bad["nodes"].OfType<JObject>().First(n => (string)n["kind"] == "primitive")["parentId"] = id;
                File.WriteAllText(Path.Combine(storage, invalid + ".json"), bad.ToString());
                Require(!(bool)Call("content_load", new JObject { ["slot"] = invalid })["success"], "Invalid saved graph was accepted.");
                Require(Owned(id).GetComponent<ManagedPanel>() == panel, "Rejected load replaced live content.");
            }
            // Historical documents contain only reconstructible containers/primitives.
            foreach (int version in new[] { 1, 2, 3 })
            {
                var legacy = (JObject)document.DeepClone(); legacy["version"] = version;
                var nodes = (JArray)legacy["nodes"];
                foreach (var node in nodes.OfType<JObject>().Where(n => (string)n["kind"] == "panel").ToArray()) node.Remove();
                foreach (var node in nodes.OfType<JObject>().Concat(new[] { (JObject)legacy["root"] })) { node.Remove("interaction"); node.Remove("motion"); node.Remove("line"); node.Remove("layout"); node.Remove("presentation"); if (version < 3) node.Remove("panel"); if (version == 1) node.Remove("appearance"); }
                File.WriteAllText(Path.Combine(storage, "legacy.json"), legacy.ToString());
                var migration = Ok("content_load", new JObject { ["slot"] = "legacy" });
                Require((bool)Get((string)primitive["entityId"])["managed"], "Legacy object did not load.");
                Undo(migration); Require((bool)Get(id)["panelIntact"], "Undoing legacy load lost the panel.");
            }
            var external = panel.transform.localPosition; panel.transform.localPosition += Vector3.right;
            Require(!(bool)Call("undo", new JObject { ["scope"] = "operation" })["success"], "Manual panel movement did not conflict with history.");
            panel.transform.localPosition = external;
            var freshRoot = new GameObject("FreshRuntime");
            try
            {
                var fresh = new SceneToolService(freshRoot.transform, storage, catalog);
                var request = new JObject { ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = "content_load",
                    ["arguments"] = new JObject { ["slot"] = "mixed" }, ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O") };
                var result = JObject.Parse(fresh.Dispatch(request.ToString())); Require((bool)result["success"], result.ToString());
                Require(fresh.RuntimeId != service.RuntimeId && freshRoot.GetComponentsInChildren<GeneratedObject>().Any(g => g.EntityId == id), "A new runtime did not recover persistent IDs.");
            }
            finally { Object.DestroyImmediate(freshRoot); }
            var requestGroup=Ok("request_begin",new JObject{["name"]="UndoPanelRequest"});
            var groupedPanel=Ok("create_panel",CreateArgs("GroupedPanel",Data()));
            Ok("update_panel",new JObject{["entityId"]=groupedPanel["entityId"],["panel"]=updatedData});
            Ok("request_end",new JObject{["requestGroupId"]=requestGroup["entityId"]});
            Ok("undo",new JObject{["scope"]="request"});
            Require((bool)Get((string)groupedPanel["entityId"])["deleted"],"Request undo retained its created panel.");
            // Explicit parent transforms exercise the same envelope/persistence path as primitives.
            var transformed=Ok("set_transform",new JObject{["entityId"]=group["entityId"],["space"]="local",["rotation"]=V(13,27,5),["scale"]=V(-2,.6f,1.2f)});
            var envelope=Ok("scene_bounds",new JObject{["entityId"]=id})["bounds"];
            foreach(var corner in panel.EnvelopeCorners())for(int axis=0;axis<3;axis++)
            {
                var key=new[]{"x","y","z"}[axis];
                Require(corner[axis]>=(float)envelope["min"][key]-.0001f&&corner[axis]<=(float)envelope["max"][key]+.0001f,"Transformed panel corner escaped its envelope.");
            }
            Undo(transformed);
            var close = panel.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Close");
            Require(catalog.CloseIcon != null && close.IconTransform.GetComponent<UnityEngine.UI.Image>().sprite == catalog.CloseIcon,
                "Close must display the catalog cancel icon.");
            var listen = panel.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Listen");
            var historyBeforeListen = Ok("history_list");
            listen.OnClick.Invoke();
            Require(listen.Label == "Stop" && voice.LastSpokenText == SpeechText.PrepareContent(panel.Content.title + "\n" + panel.Content.text) && panel.IsIntact() &&
                JToken.DeepEquals(historyBeforeListen, Ok("history_list")), "Listening must read the panel without changing history or integrity.");
            listen.OnClick.Invoke();
            Require(listen.Label == "Listen" && voice.State == SpokenReplyState.Idle, "Stop must end panel playback.");
            listen.OnClick.Invoke(); voice.SimulateFailure();
            Require(listen.Label == "Voice failed" && panel.IsIntact(), "Content failure must leave an intact panel with a local notice.");
            listen.OnClick.Invoke();
            var historyBeforeClose = (int)Ok("history_list")["total"];
            close.OnClick.Invoke();
            Require(voice.State == SpokenReplyState.Idle, "Close must stop panel playback.");
            var closeHistory = Ok("history_list");
            Require((bool)Get(id)["deleted"] && (int)closeHistory["total"] == historyBeforeClose + 1 &&
                (string)closeHistory["operations"][0]["tool"] == "delete_object", "Close must record a scene deletion.");
            Undo((JObject)closeHistory["operations"][0]);
            Require(!(bool)Get(id)["deleted"] && panel.gameObject.activeInHierarchy && panel.IsIntact(),
                "Undo Close must restore an active, intact panel.");
            var corpus = JArray.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend/bridge/test/presentation-corpus.json"))));
            var presentationData = (JObject)corpus.OfType<JObject>().First(c => (string)c["name"] == "infoPanel")["content"].DeepClone();
            var presented = Ok("ui_present", new JObject { ["name"] = "Close presentation", ["parentId"] = "Generated",
                ["position"] = V(0, 1, 2), ["rotation"] = V(), ["scale"] = V(1, 1, 1),
                ["width"] = .8, ["height"] = .7, ["presentation"] = presentationData });
            var presentationId = (string)presented["entityId"];
            var presentation = Owned(presentationId).GetComponent<ManagedPanel>();
            var presentationListen = presentation.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Listen");
            var revision = presentation.PresentationView.Revision;
            listen.OnClick.Invoke();
            presentationListen.OnClick.Invoke();
            Require(presentationListen.Label == "Stop" && presentation.IsIntact() && presentation.PresentationView.Revision == revision,
                "Presentation Listen must preserve integrity and revision.");
            Require(listen.Label == "Listen", "A second reading must release the first window.");
            Ok("ui_close", new JObject { ["entityId"] = presentationId });
            Require(voice.State == SpokenReplyState.Idle, "ui_close must stop playback.");
            Ok("ui_open", new JObject { ["entityId"] = presentationId });
            presentationListen.OnClick.Invoke();
            Owned(presentationId).GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Close").OnClick.Invoke();
            Require(voice.State == SpokenReplyState.Idle, "Presentation Close must stop playback.");
            Require((bool)Get(presentationId)["deleted"], "Presentation Close must discard rather than only hide.");
            return new JObject { ["passed"] = true, ["assertions"] = checks, ["persistenceVersion"] = (int)saved["version"] }.ToString();
        }
        finally
        {
            Object.DestroyImmediate(voiceHost);
            Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) { foreach (var file in Directory.EnumerateFiles(storage)) File.Delete(file); Directory.Delete(storage); }
        }
    }
}
