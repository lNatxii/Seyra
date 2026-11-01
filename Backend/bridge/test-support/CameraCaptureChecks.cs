using System;
using System.Threading.Tasks;
using Seyra.Cameras;
using UnityEngine;

public static class CameraCaptureChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var root = new GameObject("__CameraCaptureChecks");
        var service = root.AddComponent<CameraCaptureService>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task Finish()
        {
            float deadline = Time.realtimeSinceStartup + 7;
            while (service.IsBusy && Time.realtimeSinceStartup < deadline) await Task.Yield();
            Check(!service.IsBusy, "Service did not finish.");
        }
        try
        {
            var denied = new FakeSource { Permission = false };
            service.SetFrameSource(denied);
            service.Capture();
            Check(service.State == CameraCaptureState.Failed && denied.Begins == 0,
                "Denied permission must fail without Begin.");

            var timeout = new FakeSource { Ready = false };
            service.SetFrameSource(timeout);
            service.Capture();
            await Finish();
            Check(service.State == CameraCaptureState.Failed && timeout.Ends > 0 &&
                service.Status == "The camera did not respond. Try again.", "Timeout must fail and End.");

            var success = new FakeSource();
            service.SetFrameSource(success);
            service.Capture();
            await Finish();
            Check(service.State == CameraCaptureState.Ready && success.Ends > 0, "Capture must be Ready and End.");
            var jpeg = service.Current.Jpeg;
            Check(jpeg != null && jpeg.Length <= 512 * 1024 && jpeg[0] == 0xff && jpeg[1] == 0xd8,
                "Capture must contain a bounded JPEG.");
            var decoded = new Texture2D(2, 2);
            try { Check(decoded.LoadImage(jpeg) && decoded.width == 16 && decoded.height == 16, "JPEG must decode."); }
            finally { UnityEngine.Object.Destroy(decoded); }

            var expected = service.Current;
            Check(ReferenceEquals(service.Take(), expected) && service.Current == null, "Take must transfer Current.");

            var failed = new FakeSource { SdkFailed = true };
            service.SetFrameSource(failed);
            service.Capture();
            await Finish();
            Check(service.State == CameraCaptureState.Failed && failed.Ends > 0 &&
                service.Status == "Camera is not available on this device.", "SDK failure must report unavailable and End.");
            int photoTaken = 0;
            void CountPhoto() { photoTaken++; }
            service.PhotoTaken += CountPhoto;
            try
            {
                service.SetFrameSource(new FakeSource());
                service.Capture();
                await Finish();
                Check(service.State == CameraCaptureState.Ready && photoTaken == 1,
                    "PhotoTaken must fire exactly once for a successful capture.");
                photoTaken = 0;
                service.SetFrameSource(new FakeSource { Permission = false });
                service.Capture();
                await Finish();
                Check(service.State == CameraCaptureState.Failed && photoTaken == 0,
                    "PhotoTaken must not fire when permission is denied.");
            }
            finally { service.PhotoTaken -= CountPhoto; }
            return "CameraCaptureChecks: 6 cases passed.";
        }
        finally { UnityEngine.Object.Destroy(root); }
    }

    sealed class FakeSource : ICameraFrameSource
    {
        public bool Permission = true, Ready = true, SdkFailed;
        public int Begins, Ends;
        public bool IsSupported => true;
        public bool HasPermission => Permission;
        public bool Failed => SdkFailed;
        public bool FrameReady => Ready;
        public void RequestPermission(Action<bool> done) => done(Permission);
        public void Begin() { Begins++; Ends = 0; }
        public void End() { Ends++; }
        public void Read(Action<RawCameraFrame> done)
        {
            var rgba = new byte[16 * 16 * 4];
            for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 200; rgba[i + 1] = 90; rgba[i + 2] = 40; rgba[i + 3] = 255; }
            done(new RawCameraFrame { Rgba = rgba, Width = 16, Height = 16, TimestampUtc = DateTime.UtcNow });
        }
    }
}
