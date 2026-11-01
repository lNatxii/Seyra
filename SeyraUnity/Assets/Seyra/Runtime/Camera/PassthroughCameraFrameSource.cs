using System;
using Meta.XR;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Rendering;

namespace Seyra.Cameras
{
    public sealed class PassthroughCameraFrameSource : MonoBehaviour, ICameraFrameSource
    {
        public const bool FlipVertically = false; // Confirm with readable text on Quest before chat integration.
        const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";
        PassthroughCameraAccess access;
        PermissionCallbacks permissionCallbacks;
        int stableFrames, lastFrame = -1;
        public bool IsSupported => PassthroughCameraAccess.IsSupported;
        public bool HasPermission => Permission.HasUserAuthorizedPermission(CameraPermission);
        public bool Failed => access != null && !access.enabled;
        public bool FrameReady
        {
            get
            {
                if (access == null || !access.IsPlaying) return false;
                if (access.IsUpdatedThisFrame && lastFrame != Time.frameCount)
                {
                    lastFrame = Time.frameCount;
                    stableFrames++;
                }
                return stableFrames >= 15 && access.IsUpdatedThisFrame;
            }
        }
        public void RequestPermission(Action<bool> done)
        {
            bool completed = false;
            void Complete(bool granted) { if (completed) return; completed = true; done(granted); }
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += _ => Complete(true);
            permissionCallbacks.PermissionDenied += _ => Complete(false);
            Permission.RequestUserPermission(CameraPermission, permissionCallbacks);
        }
        public void Begin()
        {
            if (!HasPermission) throw new InvalidOperationException("Camera permission is required before Begin.");
            End();
            var cameraObject = new GameObject("PassthroughCamera");
            cameraObject.SetActive(false);
            cameraObject.transform.SetParent(transform, false);
            access = cameraObject.AddComponent<PassthroughCameraAccess>();
            access.enabled = false;
            access.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            access.RequestedResolution = new Vector2Int(1280, 960);
            access.MaxFramerate = 30;
            stableFrames = 0;
            lastFrame = -1;
            cameraObject.SetActive(true);
            access.enabled = true;
        }
        public void End()
        {
            if (access == null) return;
            access.enabled = false;
            Destroy(access.gameObject);
            access = null;
        }
        public void Read(Action<RawCameraFrame> done)
        {
            var texture = access.GetTexture();
            var frame = new RawCameraFrame {
                Width = texture.width, Height = texture.height,
                TimestampUtc = access.Timestamp.ToUniversalTime(),
                CameraPose = access.GetCameraPose(), Intrinsics = access.Intrinsics
            };
            AsyncGPUReadback.Request(texture, 0, request => {
                if (request.hasError) { done(null); return; }
                frame.Rgba = request.GetData<byte>().ToArray();
                ApplyOrientation(frame, FlipVertically);
                done(frame);
            });
        }
        static void ApplyOrientation(RawCameraFrame frame, bool flipVertically)
        {
            if (flipVertically) FlipRows(frame);
        }
        static void FlipRows(RawCameraFrame frame)
        {
            int stride = frame.Width * 4;
            var row = new byte[stride];
            for (int y = 0; y < frame.Height / 2; y++)
            {
                int top = y * stride, bottom = (frame.Height - 1 - y) * stride;
                Buffer.BlockCopy(frame.Rgba, top, row, 0, stride);
                Buffer.BlockCopy(frame.Rgba, bottom, frame.Rgba, top, stride);
                Buffer.BlockCopy(row, 0, frame.Rgba, bottom, stride);
            }
        }
    }
}
