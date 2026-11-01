using System;
using Newtonsoft.Json.Linq;
using Seyra.Cameras;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        CameraCaptureService cameraCapture;

        JObject ExecuteCamera(string tool, JObject args)
        {
            Keys(args, Array.Empty<string>());
            if (cameraCapture == null) cameraCapture = UnityEngine.Object.FindAnyObjectByType<CameraCaptureService>();
            if (cameraCapture == null) return new JObject { ["state"] = "unavailable" };
            if (tool == "camera_capture_cancel")
            {
                cameraCapture.Clear();
                return new JObject { ["state"] = "idle" };
            }
            if (tool == "camera_capture_start")
            {
                if (cameraCapture.IsBusy) return new JObject { ["state"] = "busy" };
                cameraCapture.Capture();
                return new JObject { ["state"] = "started" };
            }
            if (cameraCapture.State == CameraCaptureState.Failed)
                return new JObject { ["state"] = "failed", ["message"] = cameraCapture.Status };
            if (cameraCapture.State != CameraCaptureState.Ready) return new JObject { ["state"] = "pending" };
            var photo = cameraCapture.Take();
            try
            {
                return new JObject {
                    ["state"] = "ready", ["captureId"] = photo.Id,
                    ["width"] = photo.Width, ["height"] = photo.Height,
                    ["jpegBase64"] = Convert.ToBase64String(photo.Jpeg)
                };
            }
            finally { photo.Jpeg = null; }
        }
    }
}
