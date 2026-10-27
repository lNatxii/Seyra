using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Seyra.UI;
using Seyra.UI.Library;
using UnityEngine;
using UnityEngine.UI;

public static class LazyFollowChecks
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
    static void AtTarget(SeyraLazyFollow follow, float distance = .001f)
    {
        var target = follow.ComputeTarget();
        Check(Vector3.Distance(follow.transform.position, target.position) < distance, "Follow position differs from target.");
        Check(Quaternion.Angle(follow.transform.rotation, target.rotation) < .1f, "Follow rotation differs from target.");
    }

    public static async Task<string> Run()
    {
        Check(Application.isPlaying, "LazyFollowChecks requires Play Mode.");
        var menu = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>();
        Check(menu != null, "Spatial menu is required.");
        var follow = menu.GetComponent<SeyraLazyFollow>();
        Check(follow != null, "Menu prefab must contain SeyraLazyFollow.");
        var chat = menu.ChatXrWindow;
        Check(chat != null && chat.GetComponent<SeyraLazyFollow>() == null, "Chat must remain stationary.");
        var originalHead = follow.Head;
        var visibility = menu.GetComponent<SeyraMenuVisibility>();
        bool autoHide = visibility != null && visibility.AutoHide;
        var originalMenuPose = new Pose(menu.transform.position, menu.transform.rotation);
        var originalChatPose = new Pose(chat.transform.position, chat.transform.rotation);
        bool chatActive = chat.gameObject.activeSelf;
        var fake = new GameObject("LazyFollowCheckHead");
        var results = new List<string>();
        try
        {
            if (visibility != null) visibility.AutoHide = false;
            fake.transform.position = new Vector3(0, 1.6f, 0);
            follow.Head = fake.transform;
            follow.ResetOffset();
            follow.SnapToTarget();
            AtTarget(follow);
            results.Add("initial snap and readable forward convention");

            var stationary = menu.transform.position;
            fake.transform.rotation = Quaternion.Euler(0, 20, 0);
            await Wait(1);
            Check(follow.State == SeyraLazyFollow.FollowState.Resting && menu.transform.position == stationary,
                "Small turns must remain inside the dead zone.");
            results.Add("20 degree dead zone");

            fake.transform.rotation = Quaternion.Euler(0, 90, 0);
            await Wait(.15f);
            Check(follow.State == SeyraLazyFollow.FollowState.Waiting, "Turn must wait before following.");
            fake.transform.rotation = Quaternion.identity;
            await Wait(.15f);
            Check(follow.State == SeyraLazyFollow.FollowState.Resting && menu.transform.position == stationary,
                "Brief turns must cancel waiting.");
            results.Add("brief turn cancels delay");

            fake.transform.rotation = Quaternion.Euler(0, 90, 0);
            float turnStarted = Time.realtimeSinceStartup;
            await Wait(.7f);
            Check(follow.State == SeyraLazyFollow.FollowState.Moving, "Sustained turn must trigger movement.");
            while (follow.State != SeyraLazyFollow.FollowState.Resting && Time.realtimeSinceStartup - turnStarted < 3)
                await Task.Delay(10);
            float settlingSeconds = Time.realtimeSinceStartup - turnStarted - .6f;
            Check(follow.State == SeyraLazyFollow.FollowState.Resting, "Turn must settle.");
            Check(settlingSeconds < 1f, "90 degree turn must settle within one second after the delay: " + settlingSeconds);
            AtTarget(follow, .02f);
            results.Add("90 degree delayed movement and settling");

            fake.transform.position -= Vector3.up * .4f;
            await Wait(2);
            AtTarget(follow, .02f);
            results.Add("height change");

            follow.SimulateGrab(true);
            menu.transform.position -= Vector3.right * .3f;
            fake.transform.rotation = Quaternion.Euler(0, 180, 0);
            var heldPosition = menu.transform.position;
            await Wait(1);
            Check(follow.State == SeyraLazyFollow.FollowState.Held && menu.transform.position == heldPosition,
                "Held menu must never move.");
            var expectedOffset = Quaternion.Inverse(fake.transform.rotation) * (heldPosition - fake.transform.position);
            follow.SimulateGrab(false);
            Check(Vector3.Distance(follow.RestOffset, expectedOffset) < .001f, "Release must adopt yaw-relative offset.");
            await Wait(1);
            Check(follow.State == SeyraLazyFollow.FollowState.Resting && menu.transform.position == heldPosition,
                "Side placement must remain at rest.");
            fake.transform.rotation = Quaternion.Euler(0, 270, 0);
            await Wait(2);
            AtTarget(follow, .02f);
            results.Add("grab freeze, release adoption and following a side offset");

            follow.SimulateGrab(true);
            menu.transform.position = fake.transform.position + fake.transform.forward * 2;
            follow.SimulateGrab(false);
            Check(Mathf.Abs(new Vector2(follow.RestOffset.x, follow.RestOffset.z).magnitude - 1.3f) < .001f,
                "Release distance must be clamped to maximum.");
            follow.SimulateGrab(true);
            menu.transform.position = fake.transform.position;
            follow.SimulateGrab(false);
            Check(Mathf.Abs(new Vector2(follow.RestOffset.x, follow.RestOffset.z).magnitude - .45f) < .001f,
                "Zero-distance release must use a finite minimum offset.");
            results.Add("maximum and minimum release distance");

            follow.ResetOffset();
            follow.SnapToTarget();
            fake.transform.position += Vector3.right * 5;
            await Wait(.1f);
            AtTarget(follow);
            results.Add("teleport snaps immediately");

            follow.SimulateGrab(true);
            heldPosition = menu.transform.position;
            fake.transform.position += Vector3.right * 5;
            follow.SnapToTarget();
            await Wait(.1f);
            Check(menu.transform.position == heldPosition, "Teleport and explicit snap must respect grab ownership.");
            follow.SimulateGrab(false);
            results.Add("grab wins over teleport and explicit snap");

            fake.transform.position += Vector3.right * .5f;
            follow.enabled = false;
            follow.enabled = true;
            await Wait(.1f);
            AtTarget(follow);
            results.Add("reenable snaps");

            fake.transform.position += Vector3.right;
            Check(chat.transform.position == originalChatPose.position && chat.transform.rotation == originalChatPose.rotation,
                "Following must not move the chat.");
            var button = (Button)typeof(SeyraSpatialMenu).GetField("recenterButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            button.onClick.Invoke();
            AtTarget(follow);
            Check(follow.RestOffset == new Vector3(0, -.4f, .8f), "Recenter must restore default offset.");
            var chatTarget = fake.transform.position + fake.transform.forward * 1.2f - Vector3.up * .05f;
            Check(Vector3.Distance(chat.transform.position, chatTarget) < .001f, "Recenter must use the same explicit head for chat.");
            results.Add("stationary chat and recenter button uses shared head");

            // Temporarily remove scene fallback sources and restore them even on failure.
            heldPosition = menu.transform.position;
            var headSources = UnityEngine.Object.FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None)
                .Select(rig => rig.gameObject)
                .Concat(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                    .Where(camera => camera.CompareTag("MainCamera")).Select(camera => camera.gameObject))
                .Distinct().ToArray();
            try
            {
                foreach (var source in headSources) source.SetActive(false);
                follow.Head = null;
                Check(SeyraLazyFollow.ResolveHead() == null, "No fallback head must remain.");
                await Wait(.15f);
                follow.SnapToTarget();
                Check(menu.transform.position == heldPosition, "Missing head must suspend movement.");
                results.Add("missing head and camera suspend movement without exceptions");
            }
            finally
            {
                foreach (var source in headSources) source.SetActive(true);
                follow.Head = fake.transform;
            }

            return JsonConvert.SerializeObject(new { passed = results.Count, settlingSeconds, checks = results }, Formatting.Indented);
        }
        finally
        {
            follow.SimulateGrab(false);
            follow.Head = originalHead;
            follow.ResetOffset();
            menu.transform.SetPositionAndRotation(originalMenuPose.position, originalMenuPose.rotation);
            chat.transform.SetPositionAndRotation(originalChatPose.position, originalChatPose.rotation);
            chat.gameObject.SetActive(chatActive);
            if (visibility != null) visibility.AutoHide = autoHide;
            UnityEngine.Object.Destroy(fake);
        }
    }
}
