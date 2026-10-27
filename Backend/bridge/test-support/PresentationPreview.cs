using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;

public static class PresentationPreview
{
    public static string Create()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode.");
        var demo = UnityEngine.Object.FindFirstObjectByType<UILibraryPreview>();
        if (demo != null) demo.gameObject.SetActive(false);
        var root = new GameObject("__PresentationPreview");
        var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service = new SceneToolService(root.transform, panelCatalog: catalog);
        var corpus = JArray.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "../../Backend/bridge/test/presentation-corpus.json")));
        JObject V(float x = 0, float y = 0, float z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        var types = new[] { "stepGuide", "parameterForm" };
        for (int i = 0; i < types.Length; i++)
        {
            var example = corpus.OfType<JObject>().First(c => (string)c["name"] == types[i]);
            var response = JObject.Parse(service.Dispatch(new JObject {
                ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(),
                ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"), ["tool"] = "ui_present",
                ["arguments"] = new JObject { ["name"] = types[i], ["parentId"] = "Generated",
                    ["position"] = V(i == 0 ? -.5f : .5f, 1.4f, 1.5f), ["rotation"] = V(), ["scale"] = V(1, 1, 1),
                    ["width"] = .85, ["height"] = .75, ["presentation"] = example["content"].DeepClone() }
            }.ToString()));
            if (!(bool)response["success"]) throw new Exception(response.ToString());
        }
        return "Created two complete contract-driven presentations.";
    }
}
