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
            if (tool == "camera_marks_bind") return BindCameraMarks(args);
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

        JObject BindCameraMarks(JObject args)
        {
            Keys(args, new[] { "cardId", "rootId", "boxesId", "pointsId" });
            GameObject cardObject, root, boxes, points;
            try
            {
                cardObject = Find(args["cardId"]);
                root = Find(args["rootId"]);
                boxes = Find(args["boxesId"]);
                points = Find(args["pointsId"]);
            }
            catch (ArgumentException)
            {
                return new JObject { ["state"] = "unavailable" };
            }
            var card = cardObject.GetComponent<ManagedPanel>();
            if (card == null || card.PresentationView == null || !card.PresentationView.Values.ContainsKey("checked"))
                return new JObject { ["state"] = "unavailable" };
            var markSwitch = root.GetComponent<ObjectMarkSwitch>();
            if (markSwitch == null) markSwitch = root.AddComponent<ObjectMarkSwitch>();
            markSwitch.Bind(card, boxes, points);
            return new JObject { ["state"] = "bound" };
        }

        JObject LocateCameraRegions(JObject args)
        {
            Keys(args, new[] { "captureId", "regions" });
            var captureId = Text(args, "captureId");
            var regions = args["regions"] as JArray ?? throw new ArgumentException("regions must be an array.");
            if (regions.Count > 8) throw new ArgumentException("At most 8 regions are allowed.");
            var pixels = new List<List<Vector2>>(regions.Count);
            var boxes = new List<Vector4?>(regions.Count);
            foreach (var token in regions)
            {
                var region = token as JObject ?? throw new ArgumentException("Each region must be an object.");
                Keys(region, new[] { "points" }, new[] { "box" });
                Vector4? bounds = null;
                if (region["box"] != null)
                {
                    var box = region["box"] as JObject ?? throw new ArgumentException("box must be an object.");
                    Keys(box, new[] { "x1", "y1", "x2", "y2" });
                    float Coordinate(string key) => LineContent.Number(box[key], -float.MaxValue, float.MaxValue);
                    bounds = new Vector4(Coordinate("x1"), Coordinate("y1"), Coordinate("x2"), Coordinate("y2"));
                }
                boxes.Add(bounds);
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
            for (int regionIndex = 0; regionIndex < pixels.Count; regionIndex++)
            {
                var region = pixels[regionIndex];
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
                JToken objectBox = JValue.CreateNull();
                var bounds = boxes[regionIndex];
                if (bounds.HasValue && CaptureObjectBox.TryEstimate(photo.Depth, photo.CameraPose,
                    photo.Width, photo.Height, bounds.Value.x, bounds.Value.y, bounds.Value.z, bounds.Value.w,
                    position, out var estimated))
                {
                    var scale = generated.lossyScale;
                    objectBox = new JObject {
                        ["center"] = Vector(generated.InverseTransformPoint(estimated.Center)),
                        ["rotation"] = Vector((Quaternion.Inverse(generated.rotation) * estimated.Rotation).eulerAngles),
                        ["size"] = Vector(new Vector3(estimated.Size.x / scale.x,
                            estimated.Size.y / scale.y, estimated.Size.z / scale.z))
                    };
                    Debug.Log($"[Seyra] Object box: {estimated.Size.x:F2} x {estimated.Size.y:F2} x {estimated.Size.z:F2} m at {estimated.Distance:F2} m; pitch {estimated.PitchDegrees:F0}");
                }
                located.Add(new JObject {
                    ["box"] = objectBox,
                    ["position"] = Vector(generated.InverseTransformPoint(position)),
                    ["rotation"] = Vector((Quaternion.Inverse(generated.rotation) * rotation).eulerAngles),
                    ["valid"] = samples.Count, ["requested"] = region.Count, ["points"] = points
                });
            }
            return new JObject { ["state"] = "ready", ["regions"] = located };
        }
    }
}
