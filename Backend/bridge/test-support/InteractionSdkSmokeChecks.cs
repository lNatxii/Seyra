using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Temporary step 1 probe; deliberately independent of the production interaction feature.
public static class InteractionSdkSmokeChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "SeyraAssistant")
            throw new InvalidOperationException("Run in SeyraAssistant Play Mode.");
        var root = new GameObject("__InteractionSdkSmokeChecks");
        var errors = new List<string>();
        var passed = new List<string>();
        SceneToolService service = null;
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Assert || type == LogType.Exception || type == LogType.Error) errors.Add(message + "\n" + stack); }
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); passed.Add(message); }
        Application.logMessageReceived += Log;
        try
        {
            int initialFrame = Time.frameCount;
            foreach (bool movable in new[] { false, true })
            {
                var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.transform.SetParent(root.transform, false);
                target.transform.position = new Vector3(20, 1, 2);
                target.transform.localScale = Vector3.one * .1f;
                var rig = new GameObject("__SeyraInteraction");
                rig.SetActive(false);
                rig.transform.SetParent(target.transform, false);
                var collider = rig.AddComponent<BoxCollider>(); collider.isTrigger = true;
                var surface = rig.AddComponent<ColliderSurface>(); surface.InjectAllColliderSurface(collider);
                var ray = rig.AddComponent<RayInteractable>(); ray.InjectAllRayInteractable(surface);
                if (movable)
                {
                    var grab = rig.AddComponent<Grabbable>();
                    var transformer = rig.AddComponent<GrabFreeTransformer>();
                    grab.InjectOptionalOneGrabTransformer(transformer);
                    grab.InjectOptionalTwoGrabTransformer(transformer);
                    grab.InjectOptionalTargetTransform(target.transform);
                    grab.InjectOptionalThrowWhenUnselected(false);
                    ray.InjectOptionalPointableElement(grab);
                }
                var received = new List<PointerEventType>();
                ray.WhenPointerEventRaised += evt => received.Add(evt.Type);
                rig.SetActive(true);
                await Task.Delay(200);
                var start = target.transform.position;
                var offset = new Vector3(.2f, .1f, 0);
                void Send(PointerEventType type, Vector3 position) => ray.PublishPointerEvent(new PointerEvent(12345, type, new Pose(position, Quaternion.identity)));
                Send(PointerEventType.Hover, start);
                Send(PointerEventType.Select, start);
                Send(PointerEventType.Move, start + offset);
                Check(Vector3.Distance(target.transform.position, movable ? start + offset : start) < .0001f,
                    movable ? "Grabbable moves the target without Rigidbody." : "Ray-only events leave the target pose unchanged.");
                Send(PointerEventType.Unselect, start + offset);
                Check(received.SequenceEqual(new[] { PointerEventType.Hover, PointerEventType.Select, PointerEventType.Move, PointerEventType.Unselect }), "All four pointer events are forwarded (grabbable=" + movable + ").");
                Check(target.GetComponentInChildren<Rigidbody>(true) == null, "No Rigidbody was added (grabbable=" + movable + ").");
                Object.DestroyImmediate(target);
                Check(errors.Count == 0, "SDK lifecycle and pointer events produce no errors (grabbable=" + movable + ").");
            }
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
            service = new SceneToolService(root.transform, Path.GetFullPath(Path.Combine(Application.dataPath, "../../.utmp/10a-smoke")), catalog);
            JObject V(float x, float y, float z) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
            var result = JObject.Parse(service.Dispatch(new JObject {
                ["version"] = 1, ["type"] = "tool_request", ["requestId"] = Guid.NewGuid().ToString(), ["tool"] = "create_panel",
                ["deadlineUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"),
                ["arguments"] = new JObject { ["name"] = "SmokePanel", ["parentId"] = "Generated", ["position"] = V(20, 1, 2), ["rotation"] = V(0, 0, 0), ["scale"] = V(1, 1, 1),
                    ["panel"] = new JObject { ["version"] = 1, ["kind"] = "panel", ["title"] = "SDK smoke test", ["text"] = string.Join("\n", Enumerable.Repeat("Scrollable panel content.", 80)), ["width"] = .8, ["height"] = .6, ["fontSize"] = 24 } }
            }.ToString()));
            Check((bool)result["success"], "Managed test panel is created.");
            var panel = root.GetComponentInChildren<ManagedPanel>();
            panel.Window.GrabFrame.gameObject.SetActive(false);
            await Task.Delay(200);
            var eventSystem = EventSystem.current;
            Check(eventSystem != null, "Scene EventSystem is available.");
            var scroll = panel.GetComponentInChildren<ScrollRect>();
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1;
            ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(eventSystem) { scrollDelta = new Vector2(0, -5) }, ExecuteEvents.scrollHandler);
            Check(scroll.isActiveAndEnabled && scroll.verticalNormalizedPosition < .999f, "Scroll receives scroll input with grab frame inactive.");
            var button = panel.GetComponentsInChildren<UIActionButton>(true).First(b => b.Label == "Close");
            bool clicked = false; button.OnClick.AddListener(() => clicked = true);
            Check(button.Button.isActiveAndEnabled && button.Button.IsInteractable(), "Close button remains active and interactable with grab frame inactive.");
            ExecuteEvents.Execute(button.Button.gameObject, new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Check(clicked && !panel.gameObject.activeInHierarchy, "Close receives pointer click and discards the panel with grab frame inactive.");
            Check(Time.frameCount > initialFrame, "Play Mode advances frames during the probe.");
            Check(errors.Count == 0, "Probe produces no assertion, exception or error logs.");
            return new JObject { ["success"] = true, ["checks"] = passed.Count, ["passed"] = new JArray(passed), ["errors"] = new JArray(errors) }.ToString();
        }
        finally
        {
            service?.Dispose(); Object.DestroyImmediate(root);
            Application.logMessageReceived -= Log;
        }
    }
}
