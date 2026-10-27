using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Seyra.UI;
using UnityEngine;

/// <summary>Integration checks using the simulator's live OVRCameraRig rather than a fake head.</summary>
public static class SimulatorFollowChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode is required.");
        var rig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
        var menu = UnityEngine.Object.FindFirstObjectByType<SeyraSpatialMenu>();
        if (rig == null || menu == null) throw new InvalidOperationException("Camera rig and menu are required.");
        var follow = menu.GetComponent<SeyraLazyFollow>();
        var rigPose = new Pose(rig.transform.position, rig.transform.rotation);
        var originalHead = follow.Head;
        var chatPose = new Pose(menu.ChatXrWindow.transform.position, menu.ChatXrWindow.transform.rotation);
        var checks = new List<string>();
        try
        {
            follow.Head = rig.centerEyeAnchor;
            follow.ResetOffset();
            follow.SnapToTarget();
            rig.transform.rotation = Quaternion.AngleAxis(90, Vector3.up) * rigPose.rotation;
            await Settled(follow, "live rig yaw turn", checks);
            rig.transform.position += Vector3.right * .5f;
            await Settled(follow, "live rig translation", checks);
            rig.transform.position -= Vector3.up * .4f;
            await Settled(follow, "live rig seated height", checks);
            rig.transform.position += Vector3.forward * 5;
            int previousFrame = Time.frameCount;
            while (Time.frameCount <= previousFrame + 1) await Task.Delay(10);
            AtTarget(follow);
            checks.Add("live rig teleport snap");
            if (Vector3.Distance(menu.ChatXrWindow.transform.position, chatPose.position) > .001f ||
                Quaternion.Angle(menu.ChatXrWindow.transform.rotation, chatPose.rotation) > .1f)
                throw new Exception("Following moved the chat window.");
            checks.Add("chat remains fixed through live rig movements");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks }, Formatting.Indented);
        }
        finally
        {
            rig.transform.SetPositionAndRotation(rigPose.position, rigPose.rotation);
            follow.Head = originalHead;
            follow.ResetOffset();
            follow.SnapToTarget();
        }
    }

    static async Task Settled(SeyraLazyFollow follow, string name, List<string> checks)
    {
        int firstFrame = Time.frameCount;
        float end = Time.realtimeSinceStartup + 2;
        while (Time.realtimeSinceStartup < end) await Task.Delay(15);
        if (Time.frameCount <= firstFrame || follow.State != SeyraLazyFollow.FollowState.Resting)
            throw new Exception(name + " failed to settle with an advancing player loop.");
        AtTarget(follow);
        checks.Add(name);
    }

    static void AtTarget(SeyraLazyFollow follow)
    {
        var target = follow.ComputeTarget();
        if (Vector3.Distance(follow.transform.position, target.position) > .02f ||
            Quaternion.Angle(follow.transform.rotation, target.rotation) > 2)
            throw new Exception("Live rig target pose mismatch.");
    }
}
