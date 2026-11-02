using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Seyra.Cameras
{
    public sealed class CameraCaptureService : MonoBehaviour
    {
        public CameraCaptureState State { get; private set; }
        public CameraCapture Current { get; private set; }
        public string LastTakenId { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;
        public event Action PhotoTaken;
        readonly Queue<CameraCapture> recent = new Queue<CameraCapture>();
        public IReadOnlyCollection<CameraCapture> Recent => recent.ToArray();
        ICameraFrameSource source;
        int generation;
        bool paused, cancelOnResume, resumeCleanup;
        float deadline;
        public bool IsBusy => State == CameraCaptureState.RequestingPermission ||
            State == CameraCaptureState.Starting || State == CameraCaptureState.Capturing;

        // Set before Capture to run the same service against a simulated source.
        public void SetFrameSource(ICameraFrameSource frameSource)
        {
            if (IsBusy || paused) throw new InvalidOperationException("Cannot replace an active camera source.");
            source?.End();
            source = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        }
        void SetState(CameraCaptureState state, string status = "")
        {
            State = state;
            Status = status;
            Changed?.Invoke();
        }
        public void Capture()
        {
            if (IsBusy || paused || resumeCleanup) return;
            Clear();
            source ??= GetComponent<PassthroughCameraFrameSource>() ?? gameObject.AddComponent<PassthroughCameraFrameSource>();
            int ticket = ++generation;
            try
            {
                if (!source.IsSupported) { Fail("Camera is not available on this device."); return; }
                if (source.HasPermission) StartCamera(ticket);
                else
                {
                    SetState(CameraCaptureState.RequestingPermission);
                    source.RequestPermission(granted => {
                        if (!IsCurrent(ticket)) return;
                        if (!granted) Fail("Camera permission denied. Allow it in Settings to take photos.");
                        else StartCamera(ticket);
                    });
                }
            }
            catch (Exception) { Fail("Camera is not available on this device."); }
        }
        bool IsCurrent(int ticket) => this != null && ticket == generation && !paused;
        void StartCamera(int ticket)
        {
            if (!IsCurrent(ticket)) return;
            try
            {
                source.Begin();
                deadline = Time.realtimeSinceStartup + 5f;
                SetState(CameraCaptureState.Starting, "Taking photo… Keep looking at it.");
            }
            catch (Exception) { Fail("Camera is not available on this device."); }
        }
        void Update()
        {
            // Wait until every SDK OnApplicationPause(false) has run before disabling it.
            if (resumeCleanup)
            {
                resumeCleanup = false;
                source?.End();
                SetState(CameraCaptureState.Idle);
            }
            if (paused || !IsBusy || State == CameraCaptureState.RequestingPermission) return;
            try
            {
                if (source.Failed) { Fail("Camera is not available on this device."); return; }
                if (Time.realtimeSinceStartup >= deadline) { Fail("The camera did not respond. Try again."); return; }
                if (State != CameraCaptureState.Starting || !source.FrameReady) return;
                int ticket = generation;
                SetState(CameraCaptureState.Capturing, "Taking photo… Keep looking at it.");
                PhotoTaken?.Invoke();
                source.Read(frame => {
                    if (!IsCurrent(ticket)) return;
                    if (frame == null) { Fail("Could not read the camera image. Try again."); return; }
                    Complete(frame);
                });
            }
            catch (Exception) { Fail("Could not read the camera image. Try again."); }
        }
        void Complete(RawCameraFrame frame)
        {
            try
            {
                byte[] jpeg = null;
                using (var pixels = new NativeArray<byte>(frame.Rgba, Allocator.Temp))
                {
                    foreach (int quality in new[] { 80, 65, 50 })
                    {
                        using var encoded = ImageConversion.EncodeNativeArrayToJPG(pixels,
                            GraphicsFormat.R8G8B8A8_UNorm, (uint)frame.Width, (uint)frame.Height, 0, quality);
                        if (encoded.Length > 0 && encoded.Length <= 512 * 1024) { jpeg = encoded.ToArray(); break; }
                    }
                }
                if (jpeg == null) { Fail("The photo is too large. Try again."); return; }
                Current = new CameraCapture {
                    Id = "cap_" + Guid.NewGuid().ToString("N").Substring(0, 12), Jpeg = jpeg,
                    Width = frame.Width, Height = frame.Height, TimestampUtc = frame.TimestampUtc,
                    Camera = "left", CameraPose = frame.CameraPose, Intrinsics = frame.Intrinsics, Depth = frame.Depth
                };
                ++generation;
                source.End();
                SetState(CameraCaptureState.Ready);
            }
            catch (Exception) { Fail("Could not read the camera image. Try again."); }
        }
        void Fail(string message)
        {
            ++generation;
            source?.End();
            SetState(CameraCaptureState.Failed, message);
        }
        public CameraCapture Take()
        {
            if (State != CameraCaptureState.Ready) return null;
            var photo = Current;
            Current = null;
            LastTakenId = photo?.Id;
            if (photo != null)
            {
                recent.Enqueue(new CameraCapture {
                    Id = photo.Id, Width = photo.Width, Height = photo.Height,
                    TimestampUtc = photo.TimestampUtc, Camera = photo.Camera,
                    CameraPose = photo.CameraPose, Intrinsics = photo.Intrinsics, Depth = photo.Depth
                });
                if (recent.Count > 8) recent.Dequeue();
            }
            SetState(CameraCaptureState.Idle);
            return photo;
        }
        public CameraCapture FindRecent(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Current != null && Current.Id == id) return Current;
            foreach (var photo in recent)
                if (photo.Id == id) return photo;
            return null;
        }
        public void Clear()
        {
            ++generation;
            LastTakenId = null;
            if (Current != null) Current.Jpeg = null;
            Current = null;
            if (paused) { cancelOnResume = true; Changed?.Invoke(); return; }
            source?.End();
            SetState(CameraCaptureState.Idle);
        }
        void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            if (isPaused && IsBusy)
            {
                ++generation;
                cancelOnResume = true;
            }
            else if (!isPaused && cancelOnResume)
            {
                cancelOnResume = false;
                resumeCleanup = true;
            }
        }
        void OnDestroy()
        {
            ++generation;
            source?.End();
            if (Current != null) Current.Jpeg = null;
            Current = null;
            recent.Clear();
        }
    }
}
