using System;
using Meta.XR;
using Meta.XR.MRUtilityKit;
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
        EnvironmentRaycastManager raycaster;
        float stableSince = -1f;
        bool depthReady;
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
                if (stableFrames < 15) return false;
                if (stableSince < 0f) stableSince = Time.realtimeSinceStartup;
                if (!access.IsUpdatedThisFrame) return false;
                if (raycaster == null || !raycaster.isActiveAndEnabled) return true;
                if (Time.realtimeSinceStartup - stableSince >= 1f) return true;
                try
                {
                    var ray = access.ViewportPointToRay(new Vector2(0.5f, 0.5f), access.GetCameraPose());
                    raycaster.Raycast(ray, out var hit, CaptureDepthGrid.MaxDistance);
                    depthReady = hit.status != EnvironmentRaycastHitStatus.NotReady;
                    return depthReady;
                }
                catch (Exception)
                {
                    depthReady = false;
                    return true;
                }
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
            stableSince = -1f;
            depthReady = false;
            try
            {
                if (EnvironmentRaycastManager.IsSupported &&
                    Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                {
                    if (raycaster == null)
                        raycaster = FindAnyObjectByType<EnvironmentRaycastManager>(FindObjectsInactive.Include);
                    if (raycaster == null)
                    {
                        var depthObject = new GameObject("EnvironmentRaycast");
                        depthObject.SetActive(false);
                        depthObject.transform.SetParent(transform, false);
                        raycaster = depthObject.AddComponent<EnvironmentRaycastManager>();
                        depthObject.SetActive(true);
                    }
                    raycaster.enabled = true;
                }
            }
            catch (Exception)
            {
                if (raycaster != null) raycaster.enabled = false;
            }
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
            if (raycaster != null) raycaster.enabled = false;
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
            ReadDepth(frame);
            AsyncGPUReadback.Request(texture, 0, request => {
                if (request.hasError) { done(null); return; }
                frame.Rgba = request.GetData<byte>().ToArray();
                ApplyOrientation(frame, FlipVertically);
                done(frame);
            });
        }
        void ReadDepth(RawCameraFrame frame)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            float meters = float.NaN;
            bool? active = null;
            try
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null && rig.centerEyeAnchor != null)
                    meters = Vector3.Distance(frame.CameraPose.position, rig.centerEyeAnchor.position);
                active = MRUK.Instance?.IsWorldLockActive;
                if (!depthReady || raycaster == null || !raycaster.isActiveAndEnabled) return;
                var grid = new CaptureDepthGrid();
                var min = access.ViewportPointToRay(new Vector2(0, 0), Pose.identity).direction;
                var max = access.ViewportPointToRay(new Vector2(1, 1), Pose.identity).direction;
                grid.HasCameraModel = min.z > 0f && max.z > 0f;
                if (grid.HasCameraModel)
                {
                    grid.TanMin = new Vector2(min.x, min.y) / min.z;
                    grid.TanMax = new Vector2(max.x, max.y) / max.z;
                }
                for (int row = 0; row < CaptureDepthGrid.Rows; row++)
                for (int column = 0; column < CaptureDepthGrid.Columns; column++)
                {
                    var ray = access.ViewportPointToRay(CaptureDepthGrid.CellViewport(column, row), frame.CameraPose);
                    raycaster.Raycast(ray, out var hit, CaptureDepthGrid.MaxDistance);
                    if (hit.status != EnvironmentRaycastHitStatus.Hit) continue;
                    int index = row * CaptureDepthGrid.Columns + column;
                    grid.Points[index] = hit.point;
                    grid.Valid[index] = true;
                    grid.Hits++;
                }
                frame.Depth = grid;
            }
            catch (Exception) { frame.Depth = null; }
            finally
            {
                timer.Stop();
                Debug.Log($"[Seyra] Depth grid: {frame.Depth?.Hits ?? 0}/1200 hits in {timer.Elapsed.TotalMilliseconds:F2} ms; pose to eye {meters:F2} m; world lock {active}");
            }
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
