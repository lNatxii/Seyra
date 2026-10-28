using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PresentationChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run presentation checks in Play Mode.");
        int checks = 0;
        void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var corpus = JArray.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend/bridge/test/presentation-corpus.json"))));
        foreach (JObject entry in corpus)
        {
            bool valid = true;
            try { UIPresentationCodec.Parse((JObject)entry["content"]); } catch (ArgumentException) { valid = false; }
            Require(valid == (bool)entry["valid"], "C#/TS corpus mismatch: " + entry["name"]);
        }
        var library = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var catalog = Object.Instantiate(Resources.Load<UIPresentationCatalog>("SeyraUIPresentations"));
        var texture = new Texture2D(32, 16); var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 16), Vector2.one * .5f);
        catalog.RegisterMedia("test.image", sprite, "Registered fixture image");
        var root = new GameObject("__PresentationChecks");
        var storage = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/presentations-" + Guid.NewGuid().ToString("N")));
        var service = new SceneToolService(root.transform, storage, library, catalog);
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        JObject Raw(string tool, JObject args = null, string requestId = null) => JObject.Parse(service.Dispatch(new JObject {
            ["version"] = 1, ["type"] = "tool_request", ["requestId"] = requestId ?? Guid.NewGuid().ToString(), ["tool"] = tool,
            ["arguments"] = args ?? new JObject(), ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") }.ToString()));
        JObject Ok(string tool, JObject args = null, string requestId = null)
        {
            var r = Raw(tool, args, requestId);
            if (!(bool)r["success"])
            {
                var diagnostics = new JArray();
                foreach (var view in root.GetComponentsInChildren<ManagedPanel>(true))
                    try { diagnostics.Add(new JObject { ["name"] = view.name, ["state"] = view.InteractionState }); }
                    catch (Exception error) { diagnostics.Add(view.name + ": " + error.Message); }
                throw new Exception(tool + " " + args?["presentation"]?["type"] + " " + r + " " + diagnostics);
            }
            Require(true, "Confirmed tool"); return (JObject)r["data"];
        }
        JObject Get(string id) => Ok("ui_get", new JObject { ["entityId"] = id });
        void Undo(JObject operation) => Ok("undo", new JObject { ["scope"] = "operation", ["expectedOperationId"] = operation["operationId"] });
        ManagedPanel Panel(string id) => root.GetComponentsInChildren<ManagedPanel>(true).First(p => p.GetComponent<GeneratedObject>().EntityId == id && !p.transform.IsChildOf(root.transform.Find("__SeyraTrash")));
        JObject Args(JObject content) => new JObject { ["name"] = "Presentation " + content["type"], ["parentId"] = "Generated", ["position"] = V(0, 1.4f, 1.5f),
            ["rotation"] = V(), ["scale"] = V(1, 1, 1), ["width"] = .8, ["height"] = .7, ["presentation"] = content.DeepClone() };
        var ids = new System.Collections.Generic.Dictionary<string, string>();
        try
        {
            Require(((JArray)Ok("ui_catalog")["types"]).Count == 12, "Missing presentation types.");
            Require((int)Ok("ui_media_list", new JObject { ["offset"] = 0, ["limit"] = 20 })["total"] >= 1, "Registered media was not listed.");
            foreach (var entry in corpus.OfType<JObject>().Where(e => (bool)e["valid"]))
            {
                var data = (JObject)entry["content"].DeepClone();
                if ((string)data["type"] == "gallery") data["images"][0]["imageRef"] = "test.image";
                var args = Args(data); var requestId = Guid.NewGuid().ToString();
                var result = Ok("ui_present", args, requestId);
                ids[(string)data["type"]] = (string)result["entityId"];
                Require((bool)result["panelIntact"], "Initial presentation is not intact: " + data["type"]);
                Require(JToken.DeepEquals(result, Ok("ui_present", args, requestId)), "Presentation creation was not deduplicated.");
            }
            await Task.Delay(200);
            foreach (var pair in ids)
            {
                var result = Get(pair.Value);
                if (!(bool)result["panelIntact"])
                {
                    var managed = Panel(pair.Value); var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var baseline = JArray.Parse((string)typeof(ManagedPanel).GetField("structure", flags).GetValue(managed));
                    var actual = JArray.Parse((string)typeof(ManagedPanel).GetMethod("Structure", flags).Invoke(managed, null));
                    var differences = new JArray(actual.Where(a => !baseline.Any(b => JToken.DeepEquals(a, b))));
                    var expected = (System.Collections.Generic.Dictionary<TMP_Text, string>)typeof(ManagedPanel).GetField("expectedPresentationText", flags).GetValue(managed);
                    var textErrors = new JArray(expected.Where(e => e.Key == null || e.Key.text != e.Value || e.Key.richText).Select(e => new JObject { ["expected"] = e.Value, ["actual"] = e.Key != null ? e.Key.text : null }));
                    throw new Exception("First-frame integrity failed: " + pair.Key + " structure=" + differences + " texts=" + textErrors);
                }
                Require(true, "First-frame integrity");
                Require(Panel(pair.Value).Window.GrabFrame.Grabbable.Transform == Panel(pair.Value).transform, "Grab targets an internal view.");
                Require(Panel(pair.Value).GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Missing presentation script.");
            }
            Require(((JArray)Get(ids["gallery"])["missingMedia"]).Count == 1, "Missing/registered media handling is incorrect.");
            var gallery = Panel(ids["gallery"]); var galleryView = gallery.PresentationView;
            galleryView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Ampliar / restablecer imagen").OnClick.Invoke();
            Require((int)gallery.InteractionState["zoom"]["0"] == 2, "Image zoom was not captured.");
            galleryView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Siguiente").OnClick.Invoke();
            Require((int)Get(ids["gallery"])["interaction"]["index"] == 1, "Gallery pager did not advance.");
            var checklist = Panel(ids["checklist"]);
            var check = checklist.PresentationView.GetComponentsInChildren<UIField>(true).First(f => f.Control is Toggle);
            ((Toggle)check.Control).isOn = true;
            Require((string)checklist.InteractionState["values"]["manual"] == "true", "Checklist interaction failed.");
            var guide = Panel(ids["stepGuide"]);
            ((Toggle)guide.PresentationView.GetComponentsInChildren<UIField>(true).First(f => f.Control is Toggle).Control).isOn = true;
            guide.PresentationView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Siguiente").OnClick.Invoke();
            var diagnostic = Panel(ids["diagnostic"]);
            diagnostic.PresentationView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Sí").OnClick.Invoke();
            Require((string)diagnostic.InteractionState["nodeId"] == "found", "Diagnostic navigation failed.");
            var choice = Panel(ids["choiceSelector"]);
            var choices = choice.PresentationView.GetComponentsInChildren<UIField>(true).Where(f => f.Control is Toggle).ToArray();
            ((Toggle)choices[0].Control).isOn = true; ((Toggle)choices[1].Control).isOn = true;
            Require((string)choice.InteractionState["values"]["matte"] == "false", "Single-choice selection was not exclusive.");
            choice.PresentationView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Elegir acabado").OnClick.Invoke();
            Require((bool)choice.InteractionState["submitted"] && (string)choice.InteractionState["submission"]["selectedIds"][0] == "gloss", "Typed selection submission failed.");
            var form = Panel(ids["parameterForm"]);
            var inputs = form.PresentationView.GetComponentsInChildren<UIField>(true).Where(f => f.Control is TMP_InputField).ToArray();
            ((TMP_InputField)inputs[0].Control).text = "invalid";
            var submit = form.PresentationView.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == "Registrar"); submit.OnClick.Invoke();
            Require(!(bool)form.InteractionState["submitted"], "Invalid form submitted.");
            ((TMP_InputField)inputs[0].Control).text = "2.5e2"; ((TMP_InputField)inputs[1].Control).text = "  Texto <b>literal</b>  ";
            form.PresentationView.GetComponentInChildren<UIMetaDropdown>(true).SetValue(1, true); submit.OnClick.Invoke();
            Require((bool)form.InteractionState["submitted"] && (double)form.InteractionState["submission"]["width"] == 250, "Numeric exponent or typed form submission failed.");
            var beforeForm = form.InteractionState;
            var beforeContent = form.Presentation.ToJson();
            var replacement = (JObject)beforeContent["presentation"].DeepClone(); replacement["title"] = "Updated form";
            var edit = Ok("ui_update", new JObject { ["entityId"] = ids["parameterForm"], ["width"] = .9, ["height"] = .8, ["presentation"] = replacement });
            Require(!(bool)edit["interaction"]["submitted"], "Full update did not reset local state.");
            Undo(edit); form = Panel(ids["parameterForm"]);
            Require(JToken.DeepEquals(beforeContent, form.Presentation.ToJson()) && JToken.DeepEquals(beforeForm["values"], form.InteractionState["values"]) && (bool)form.InteractionState["submitted"], "Content undo did not restore the latest user interaction.");
            var move = Ok("set_transform", new JObject { ["entityId"] = ids["parameterForm"], ["space"] = "local", ["position"] = V(2, 1, 2) });
            var textField = form.PresentationView.GetComponentsInChildren<UIField>(true).Where(f => f.Control is TMP_InputField).Last();
            ((TMP_InputField)textField.Control).text = "After moving";
            Undo(move); Require((string)form.InteractionState["values"]["note"] == "After moving", "Transform undo discarded user input.");
            Ok("ui_close", new JObject { ["entityId"] = ids["gallery"] });
            Require(!(bool)Get(ids["gallery"])["interaction"]["visible"], "Close did not hide the window.");
            var snapshots = ids.ToDictionary(p => p.Key, p => Panel(p.Value).InteractionState);
            var saved = Ok("content_save", new JObject { ["slot"] = "all" });
            Require((int)saved["version"] == 5 && (int)saved["nodeCount"] == 12, "Presentations were not saved as atomic v5 nodes.");
            var loaded = Ok("content_load", new JObject { ["slot"] = "all" }); await Task.Delay(200);
            foreach (var pair in ids)
            {
                Require((bool)Get(pair.Value)["panelIntact"], "Loaded presentation is not intact: " + pair.Key);
                var state = Panel(pair.Value).InteractionState;
                Require(JToken.DeepEquals(snapshots[pair.Key]["values"], state["values"]) && JToken.DeepEquals(snapshots[pair.Key]["zoom"], state["zoom"]) && (int)snapshots[pair.Key]["index"] == (int)state["index"], "Interaction did not round-trip: " + pair.Key);
            }
            Require(!(bool)Panel(ids["gallery"]).InteractionState["visible"], "Hidden visibility did not persist.");
            Ok("ui_open", new JObject { ["entityId"] = ids["gallery"] });
            Undo(loaded); Require(Panel(ids["gallery"]) == gallery, "Load undo lost original presentation instances.");
            var removed = Ok("ui_remove", new JObject { ["entityId"] = ids["checklist"] });
            Require((bool)removed["deleted"], "ui_remove did not use trash."); Undo(removed);
            Require((string)Panel(ids["checklist"]).InteractionState["values"]["manual"] == "true", "Remove undo lost local state.");
            foreach (var corrupt in new[] { "unknownValue", "invalidIndex", "invalidSubmission", "futureState" })
            {
                var document = JObject.Parse(File.ReadAllText(Path.Combine(storage, "all.json")));
                var node = document["nodes"].OfType<JObject>().Single(n => (string)n["presentation"]["content"]["presentation"]["type"] == "parameterForm");
                var state = node["presentation"]["state"];
                if (corrupt == "unknownValue") state["values"]["unknown"] = "test";
                if (corrupt == "invalidIndex") state["index"] = 9;
                if (corrupt == "invalidSubmission") { state["submitted"] = true; state["submission"] = new JObject { ["width"] = 999 }; }
                if (corrupt == "futureState") state["version"] = 2;
                File.WriteAllText(Path.Combine(storage, "bad.json"), document.ToString());
                Require(!(bool)Raw("content_load", new JObject { ["slot"] = "bad" })["success"], "Invalid state was loaded: " + corrupt);
                Require(Panel(ids["gallery"]) == gallery, "Rejected load replaced the live graph.");
            }
            var invalidContent = (JObject)replacement.DeepClone(); invalidContent["unknown"] = true;
            Require(!(bool)Raw("ui_update", new JObject { ["entityId"] = ids["parameterForm"], ["width"] = .8, ["height"] = .7, ["presentation"] = invalidContent })["success"], "Unknown presentation content was accepted.");
            Require(((JArray)Ok("ui_list")["presentations"]).Count == 12, "ui_list lost live views.");
            var restoredChecklist = Panel(ids["checklist"]);
            var previousState = restoredChecklist.InteractionState;
            var same = restoredChecklist.Presentation.ToJson();
            var sameUpdate = Ok("ui_update", new JObject { ["entityId"] = ids["checklist"], ["width"] = same["width"], ["height"] = same["height"], ["presentation"] = same["presentation"] });
            Undo(sameUpdate);
            Require(JToken.DeepEquals(previousState["values"], restoredChecklist.InteractionState["values"]), "Identical replacement undo lost interaction.");
            var info = (JObject)corpus.OfType<JObject>().First(c => (string)c["name"] == "infoPanel")["content"].DeepClone();
            for (int i = 0; i < 4; i++)
            {
                info["id"] = "capacity-" + i;
                Ok("ui_present", Args(info));
            }
            info["id"] = "over-capacity";
            Require(!(bool)Raw("ui_present", Args(info))["success"], "Presentation capacity was not enforced.");
            Require(((JArray)Ok("ui_list")["presentations"]).Count == 16, "Rejected creation changed live count.");
            return new JObject { ["passed"] = true, ["assertions"] = checks, ["types"] = ids.Count, ["corpus"] = corpus.Count }.ToString();
        }
        finally
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(catalog); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            if (Directory.Exists(storage)) { foreach (var file in Directory.EnumerateFiles(storage)) File.Delete(file); Directory.Delete(storage); }
        }
    }
}
