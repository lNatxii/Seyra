using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Seyra.UI;
using UnityEngine;

public static class MenuVisibilityChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static async Task Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        int frame = Time.frameCount;
        while (Time.realtimeSinceStartup < end) await Task.Delay(15);
        Check(Time.frameCount > frame, "Player loop must advance during checks.");
    }

    public static async Task<string> Run()
    {
        Check(Application.isPlaying, "MenuVisibilityChecks requires Play Mode.");
        var menu = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>();
        Check(menu != null, "Spatial menu is required.");
        var visibility = menu.GetComponent<SeyraMenuVisibility>();
        var follow = menu.GetComponent<SeyraLazyFollow>();
        Check(visibility != null && follow != null && visibility.ChatWindow != null, "Menu visibility references are required.");
        var chat = visibility.ChatWindow;
        var originalHead = follow.Head;
        var menuPose = new Pose(menu.transform.position, menu.transform.rotation);
        var chatPose = new Pose(chat.transform.position, chat.transform.rotation);
        bool chatActive = chat.gameObject.activeSelf, autoHide = visibility.AutoHide;
        bool background = Application.runInBackground;
        var parts = Enumerable.Range(0, menu.transform.childCount).Select(i => menu.transform.GetChild(i).gameObject).ToArray();
        var fake = new GameObject("MenuVisibilityCheckHead");
        try
        {
            Application.runInBackground = true;
            visibility.AutoHide = false;
            fake.transform.position = new Vector3(0, 1.6f, 0);
            follow.Head = fake.transform;
            chat.transform.SetPositionAndRotation(new Vector3(0, 1.35f, 1.25f), Quaternion.identity);
            chat.Open();
            visibility.AutoHide = true;
            await Wait(.6f);
            Check(visibility.State == SeyraMenuVisibility.VisibilityState.Hidden && parts.All(p => !p.activeSelf),
                "Chat in view must hide all menu parts.");
            Check(menu.gameObject.activeInHierarchy, "Menu root must remain active.");

            fake.transform.rotation = Quaternion.Euler(0, 60, 0);
            await Wait(.7f);
            Check(visibility.State == SeyraMenuVisibility.VisibilityState.Shown && parts.All(p => p.activeSelf),
                "Looking away must restore menu parts.");

            fake.transform.rotation = Quaternion.identity;
            await Wait(.6f);
            Check(visibility.State == SeyraMenuVisibility.VisibilityState.Hidden, "Menu must hide again before closing chat.");
            chat.Close();
            int closedFrame = Time.frameCount;
            while (Time.frameCount <= closedFrame) await Task.Delay(1);
            Check(visibility.State == SeyraMenuVisibility.VisibilityState.Shown, "Closing chat must show the menu on the next frame.");
        }
        finally
        {
            visibility.AutoHide = false;
            follow.Head = originalHead;
            menu.transform.SetPositionAndRotation(menuPose.position, menuPose.rotation);
            chat.transform.SetPositionAndRotation(chatPose.position, chatPose.rotation);
            chat.gameObject.SetActive(chatActive);
            visibility.AutoHide = autoHide;
            Application.runInBackground = background;
            UnityEngine.Object.Destroy(fake);
        }
        Check(follow.Head == originalHead && menu.transform.position == menuPose.position &&
            menu.transform.rotation == menuPose.rotation && chat.transform.position == chatPose.position &&
            chat.transform.rotation == chatPose.rotation && chat.gameObject.activeSelf == chatActive,
            "Checks must restore the original scene state.");
        return JsonConvert.SerializeObject(new { passed = 4, checks = new[] {
            "chat in view hides parts and keeps root active", "60 degree turn restores menu",
            "closing chat shows menu next frame", "scene state restored" } }, Formatting.Indented);
    }
}
