using System;

namespace Seyra.Cameras
{
    public interface ICameraFrameSource
    {
        bool IsSupported { get; }
        bool HasPermission { get; }
        void RequestPermission(Action<bool> done);
        void Begin();
        void End();
        bool Failed { get; }
        bool FrameReady { get; }
        void Read(Action<RawCameraFrame> done);
    }
}
