using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CardCheckChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run card checks in Play Mode.");
        var library = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var catalog = Resources.Load<UIPresentationCatalog>("SeyraUIPresentations");
        var root = new GameObject("__CardCheckChecks");
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/card-checks-" + Guid.NewGuid().ToString("N")));
        var service = new SceneToolService(root.transform, storage, library, catalog);
        int assertions = 0;
        void Require(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        JObject Raw(string tool, JObject args = null) => JObject.Parse(service.Dispatch(new JObject {
            ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = tool,
            ["arguments"] = args ?? new JObject(), ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args = null)
        { var response = Raw(tool, args); Require((bool)response["success"], response.ToString()); return (JObject)response["data"]; }
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Data(string kind = "panel", string label = "Got it") => new JObject {
            ["version"] = 1, ["kind"] = kind, ["title"] = kind == "panel" ? "Eggs" : "", ["text"] = "Two eggs",
            ["width"] = .4, ["height"] = .28, ["fontSize"] = 24,
            ["check"] = new JObject { ["label"] = label } };
        JObject Args(string name) => new JObject { ["name"] = name, ["parentId"] = "Generated",
            ["position"] = V(0, 1, 2), ["rotation"] = V(), ["scale"] = V(1, 1, 1) };
        JObject Get(string id) => Ok("scene_get", new JObject { ["entityId"] = id });
        void Undo(JObject operation) => Ok("undo", new JObject { ["scope"] = "operation", ["expectedOperationId"] = operation["operationId"] });
        ManagedPanel Panel(string id) => root.GetComponentsInChildren<GeneratedObject>(true).First(g => g.EntityId == id).GetComponent<ManagedPanel>();
        try
        {
            var args = Args("Eggs"); args["panel"] = Data();
            var created = Ok("create_panel", args); var id = (string)created["entityId"];
            Require((string)Get(id)["panel"]["check"]["label"] == "Got it" && !(bool)Get(id)["panel"]["check"]["completed"], "Panel check did not round-trip.");
            var invalid = Args("Invalid"); invalid["panel"] = Data("label");
            Require(!(bool)Raw("create_panel", invalid)["success"], "Label accepted a check.");
            invalid["panel"] = Data(label: new string('x', 25));
            Require(!(bool)Raw("create_panel", invalid)["success"], "Long label was accepted.");
            await Task.Delay(150);
            var panel = Panel(id); var toggle = panel.GetComponentInChildren<Toggle>(true);
            toggle.isOn = true;
            Require(panel.IsIntact() && (bool)Get(id)["panel"]["check"]["completed"], "Click damaged integrity or did not update state.");
            var moved = Ok("set_transform", new JObject { ["entityId"] = id, ["space"] = "local", ["position"] = V(1, 1, 2) });
            toggle.isOn = false; Undo(moved);
            Require(!Panel(id).Content.checkCompleted, "Undo move lost the newer local mark.");
            toggle.isOn = true;
            var updateData = Data(); updateData["text"] = "Three eggs";
            var updated = Ok("update_panel", new JObject { ["entityId"] = id, ["panel"] = updateData });
            Require((bool)updated["panel"]["check"]["completed"] && updateData["check"]["completed"] == null, "Update lost the mark or mutated input.");
            toggle.isOn = false; Undo(updated);
            Require(Panel(id).Content.checkCompleted && Panel(id).Content.text == "Two eggs", "Undo update did not restore content and its current pre-update mark.");
            var presentation = new JObject { ["type"] = "infoPanel", ["id"] = "film", ["title"] = "Iron Man", ["description"] = "A film",
                ["check"] = new JObject { ["label"] = "Watched" } };
            var uiArgs = Args("Film"); uiArgs["presentation"] = presentation; uiArgs["width"] = .4; uiArgs["height"] = .5;
            var uiId = (string)Ok("ui_present", uiArgs)["entityId"];
            var uiToggle = Panel(uiId).PresentationView.GetComponentInChildren<Toggle>(true); uiToggle.isOn = true;
            var uiUpdate = new JObject { ["entityId"] = uiId, ["width"] = .4, ["height"] = .5, ["presentation"] = presentation.DeepClone() };
            uiUpdate["presentation"]["description"] = "Updated summary";
            var uiUpdated = Ok("ui_update", uiUpdate);
            Require((string)Get(uiId)["interaction"]["values"]["checked"] == "true", "Presentation update lost the user's mark.");
            Panel(uiId).PresentationView.Values["checked"] = "false"; Undo(uiUpdated);
            Require((string)Get(uiId)["interaction"]["values"]["checked"] == "true", "Presentation undo did not restore the mark.");
            Panel(uiId).PresentationView.GetComponentInChildren<Toggle>(true).isOn = false;
            var checks = Ok("card_checks");
            Require((int)checks["total"] == 2 && (int)checks["completedCount"] == 1 && (string)checks["cards"][0]["entityId"] == id &&
                (bool)checks["cards"][0]["completed"] && !(bool)checks["cards"][1]["completed"], "Joint query returned incorrect marks or order: " + checks);
            Require((int)Ok("card_checks", new JObject { ["rootId"] = id })["total"] == 1, "Query scope is incorrect.");
            Ok("content_save", new JObject { ["slot"] = "checks" });
            var deleted = Ok("delete_object", new JObject { ["entityId"] = id });
            Require((int)Ok("card_checks")["total"] == 1, "Trash was included.");
            Undo(deleted); Require(Panel(id).Content.checkCompleted, "Delete undo lost the mark.");
            Ok("content_load", new JObject { ["slot"] = "checks" });
            Require(Panel(id).Content.checkCompleted && (string)Get(uiId)["interaction"]["values"]["checked"] == "false", "Save/load lost panel or presentation marks.");
            return new JObject { ["passed"] = true, ["assertions"] = assertions }.ToString();
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (Directory.Exists(storage)) { foreach (var file in Directory.EnumerateFiles(storage)) File.Delete(file); Directory.Delete(storage); }
        }
    }
}
