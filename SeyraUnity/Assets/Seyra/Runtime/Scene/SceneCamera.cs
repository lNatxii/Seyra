using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Seyra.Cameras;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        CameraCaptureService cameraCapture;

        JObject ExecuteCamera(string tool, JObject args)
        {
            if (tool == "camera_locate") return LocateCameraRegions(args);
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

        JObject LocateCameraRegions(JObject args)
        {
            Keys(args, new[] { "captureId", "regions" });
            var captureId = Text(args, "captureId");
            var regions = args["regions"] as JArray ?? throw new ArgumentException("regions must be an array.");
            if (regions.Count > 8) throw new ArgumentException("At most 8 regions are allowed.");
            var pixels = new List<List<Vector2>>(regions.Count);
            foreach (var token in regions)
            {
                var region = token as JObject ?? throw new ArgumentException("Each region must be an object.");
                Keys(region, new[] { "points" });
                var points = region["points"] as JArray ?? throw new ArgumentException("points must be an array.");
                if (points.Count > 32) throw new ArgumentException("At most 32 points per region are allowed.");
                var samples = new List<Vector2>(points.Count);
                foreach (var pointToken in points)
                {
                    var point = pointToken as JObject ?? throw new ArgumentException("Each point must contain x,y.");
                    Keys(point, new[] { "x", "y" });
                    var vector = ReadVector(new JObject { ["x"] = point["x"].DeepClone(), ["y"] = point["y"].DeepClone(), ["z"] = 0 });
                    samples.Add(new Vector2(vector.x, vector.y));
                }
                pixels.Add(samples);
            }

            var located = new JArray();
            if (cameraCapture == null) cameraCapture = UnityEngine.Object.FindAnyObjectByType<CameraCaptureService>();
            if (cameraCapture == null) return new JObject { ["state"] = "unavailable", ["regions"] = located };
            var photo = cameraCapture.FindRecent(captureId);
            if (photo?.Depth == null) return new JObject { ["state"] = "no_depth", ["regions"] = located };
            Invertible(generated);
            foreach (var region in pixels)
            {
                var samples = new List<Vector3>(region.Count);
                foreach (var pixel in region)
                    if (photo.Depth.TryGet(pixel.x, pixel.y, photo.Width, photo.Height, out var point)) samples.Add(point);
                if (!CaptureDepthGrid.TryMedian(samples, region.Count, out var position))
                {
                    located.Add(JValue.CreateNull());
                    continue;
                }
                var rotation = Quaternion.LookRotation(position - photo.CameraPose.position, Vector3.up);
                var points = new JArray();
                foreach (var point in samples) points.Add(Vector(generated.InverseTransformPoint(point)));
                located.Add(new JObject {
                    ["position"] = Vector(generated.InverseTransformPoint(position)),
                    ["rotation"] = Vector((Quaternion.Inverse(generated.rotation) * rotation).eulerAngles),
                    ["valid"] = samples.Count, ["requested"] = region.Count, ["points"] = points
                });
            }
            return new JObject { ["state"] = "ready", ["regions"] = located };
        }
    }
}
