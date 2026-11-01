using System;
using Meta.XR;
using UnityEngine;

namespace Seyra.Cameras
{
    public enum CameraCaptureState { Idle, RequestingPermission, Starting, Capturing, Ready, Failed }

    public sealed class CameraCapture
    {
        public string Id;
        public byte[] Jpeg;
        public int Width, Height;
        public DateTime TimestampUtc;
        public string Camera;
        public Pose CameraPose;
        public PassthroughCameraAccess.CameraIntrinsics Intrinsics;
    }

    public sealed class RawCameraFrame
    {
        public byte[] Rgba;
        public int Width, Height;
        public DateTime TimestampUtc;
        public Pose CameraPose;
        public PassthroughCameraAccess.CameraIntrinsics Intrinsics;
    }
}
