using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Seyra.Spatial;
using Seyra.UI;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class HandMenuChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task<string> Run()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var scene = SceneManager.CreateScene("__HandMenuChecks");
        var app = new GameObject("__HandMenuApp"); SceneManager.MoveGameObjectToScene(app, scene); app.SetActive(false);
        var head = new GameObject("__HandMenuHead"); SceneManager.MoveGameObjectToScene(head, scene);
        var anchor = new GameObject("__LeftController"); SceneManager.MoveGameObjectToScene(anchor, scene);
        head.transform.position = new Vector3(0, 1.6f, 0);
        anchor.transform.position = new Vector3(-.2f, 1.2f, .4f);
        try
        {
            // 1. Facing, show/hide delays and the hysteresis band.
            var palm = new Pose(Vector3.forward, Quaternion.Euler(90, 0, 0));
            var gate = new HandMenuGate();
            float facing = HandMenuPose.PalmFacing(palm, Vector3.down, Vector3.zero);
            Check(facing > .99f && !gate.Tick(facing, .1f) && gate.Tick(facing, .06f), "Palm must show only after its delay.");
            for (int i = 0; i < 8; i++) Check(gate.Tick(i % 2 == 0 ? .56f : .74f, .1f), "Hysteresis must preserve visibility.");
            Check(gate.Tick(-facing, .15f) && !gate.Tick(-facing, .16f), "Turned palm must hide after its delay.");
            for (int i = 0; i < 8; i++) Check(!gate.Tick(i % 2 == 0 ? .56f : .74f, .1f), "Hysteresis must preserve hidden state.");

            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seyra/UI/Panels/SeyraSpatialMenu.prefab"), app.transform);
            var hand = root.GetComponent<SeyraHandMenu>();
            var window = root.GetComponentInChildren<UIXRWindow>(true);
            var menu = root.GetComponentInChildren<SeyraSpatialMenu>(true);
            var chatRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seyra/UI/Panels/SeyraSpatialChatWindow.prefab"), app.transform);
            var chat = chatRoot.GetComponent<SeyraSpatialChatWindow>(); chat.enabled = false;
            var chatWindow = chatRoot.GetComponent<UIXRWindow>();
            var placement = SpatialPlacementService.ForScene(scene);
            var follow = app.AddComponent<SpatialFollowController>();
            follow.Configure(head.transform, chat, chatWindow, placement); follow.UseTestTracking(true);
            menu.Configure(chat, chatWindow);
            typeof(SeyraSpatialMenu).GetField("spatialFollow", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(menu, follow);
            app.SetActive(true);
            hand.UseTestControllerTracking(head.transform, anchor.transform);

            // 2. Controller handler toggles twice, anchors the canvas center and faces the viewer.
            Check(!hand.IsVisible && root.activeSelf, "Hidden window must leave its controller active.");
            hand.ToggleMenu();
            var corners = new Vector3[4]; ((RectTransform)window.Canvas.transform).GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * .5f;
            var expected = anchor.transform.TransformPoint(new Vector3(0, .08f, .05f));
            Check(hand.IsVisible && Vector3.Distance(center, expected) < .001f &&
                Vector3.Dot(window.Canvas.transform.forward, (center - head.transform.position).normalized) > .999f,
                "Controller menu must anchor its canvas center and face the viewer.");
            hand.ToggleMenu(); Check(!hand.IsVisible, "Second controller action must close the window.");

            // 3. Physical width from actual canvas corners.
            float width = Vector3.Distance(corners[0], corners[3]);
            Check(width >= .13f && width <= .17f, "Hand menu width must be between 0.13 and 0.17 meters.");

            // 4. Bring a chat from three meters behind a simulated head.
            chatRoot.transform.position = head.transform.position - head.transform.forward * 3;
            chatWindow.Close(); menu.OnBringChatClicked();
            Check(chatRoot.activeSelf && Vector3.Dot(chatRoot.transform.position - head.transform.position, head.transform.forward) > 0 &&
                menu.StatusText.Contains("Chat delante de ti"), "Bring chat must recenter and report success.");

            // 5. A visible menu never adds a placement reservation.
            chatWindow.Close(); hand.ToggleMenu();
            Check(hand.IsVisible && placement.Reservations().Count == 0, "The visible menu must not reserve space.");
            return "Hand menu basic checks passed: 5 (width " + width.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " m).";
        }
        finally
        {
            Object.DestroyImmediate(app); Object.DestroyImmediate(head); Object.DestroyImmediate(anchor);
            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null) while (!unload.isDone) await Task.Yield();
        }
    }
}
