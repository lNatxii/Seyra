using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seyra.Cameras
{
    public static class CameraOverlayGeometry
    {
        public struct Surface
        {
            public Vector3 Point, Normal;
            public string Plane;
            public int Samples, Inliers;
        }

        public static bool TryFitPlane(CaptureDepthGrid grid, Pose pose, int width, int height,
            IReadOnlyList<Vector2> pixels, out Surface surface)
        {
            surface = default;
            if (grid == null || !grid.HasCameraModel) return false;
            var samples = new List<Vector3>();
            foreach (var pixel in pixels)
                if (grid.TryGet(pixel.x, pixel.y, width, height, out var point)) samples.Add(point);
            if (!CaptureDepthGrid.TryMedian(samples, pixels.Count, out var median)) return false;
            var forward = pose.rotation * Vector3.forward;
            surface = new Surface { Point = median, Normal = -forward, Plane = "frontal", Samples = samples.Count, Inliers = samples.Count };
            if (samples.Count >= 6)
            {
                var random = new System.Random(38);
                var best = new List<Vector3>();
                Vector3 normal = default;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    var a = samples[random.Next(samples.Count)];
                    var b = samples[random.Next(samples.Count)];
                    var c = samples[random.Next(samples.Count)];
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 0.00000001f) continue;
                    n.Normalize();
                    if (Vector3.Dot(n, pose.position - median) < 0) n = -n;
                    if (Vector3.Angle(n, pose.position - median) > 60) continue;
                    var inliers = samples.FindAll(p => Mathf.Abs(Vector3.Dot(n, p - a)) <= 0.025f);
                    if (inliers.Count > best.Count) { best = inliers; normal = n; }
                }
                if (best.Count >= samples.Count * 0.6f && CaptureDepthGrid.TryMedian(best, best.Count, out var center))
                {
                    if (Mathf.Abs(normal.y) <= Mathf.Sin(8 * Mathf.Deg2Rad)) normal = new Vector3(normal.x, 0, normal.z).normalized;
                    surface = new Surface { Point = center, Normal = normal, Plane = "fitted", Samples = samples.Count, Inliers = best.Count };
                    return true;
                }
            }
            var distances = new List<Vector3>();
            foreach (var sample in samples) distances.Add(new Vector3(Vector3.Dot(sample - pose.position, forward), 0, 0));
            CaptureDepthGrid.TryMedian(distances, distances.Count, out var depth);
            surface.Point = pose.position + forward * depth.x;
            return true;
        }

        public static string TryBuildMesh(CaptureDepthGrid grid, Pose pose, int width, int height, Vector4 rect,
            Surface surface, out Mesh mesh, out Vector2 size, out float distance)
        {
            mesh = null; size = default; distance = 0;
            if (grid == null || !grid.HasCameraModel) return "no_depth";
            var vertices = new Vector3[17 * 17];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[16 * 16 * 6];
            for (int y = 0; y < 17; y++) for (int x = 0; x < 17; x++)
            {
                float u = x / 16f, v = y / 16f;
                var ray = grid.PixelDirection(Mathf.Lerp(rect.x, rect.z, u), Mathf.Lerp(rect.y, rect.w, v), width, height, pose);
                float denominator = Vector3.Dot(surface.Normal, ray.normalized);
                if (Mathf.Abs(denominator) < 0.05f) return "too_large";
                float t = Vector3.Dot(surface.Normal, surface.Point - pose.position) / Vector3.Dot(surface.Normal, ray);
                if (t <= 0 || float.IsNaN(t) || float.IsInfinity(t)) return "no_depth";
                vertices[y * 17 + x] = pose.position + ray * t;
                uv[y * 17 + x] = new Vector2(u, 1 - v);
                if (x < 16 && y < 16)
                {
                    int offset = (y * 16 + x) * 6, i = y * 17 + x;
                    triangles[offset] = i; triangles[offset + 1] = i + 1; triangles[offset + 2] = i + 17;
                    triangles[offset + 3] = i + 1; triangles[offset + 4] = i + 18; triangles[offset + 5] = i + 17;
                }
            }
            size = new Vector2(Mathf.Max(Vector3.Distance(vertices[0], vertices[16]), Vector3.Distance(vertices[272], vertices[288])),
                Mathf.Max(Vector3.Distance(vertices[0], vertices[272]), Vector3.Distance(vertices[16], vertices[288])));
            if (size.x > 4 || size.y > 4) return "too_large";
            if (size.x < 0.05f || size.y < 0.05f) return "too_small";
            distance = Vector3.Distance(pose.position, surface.Point);
            mesh = new Mesh { name = "Camera overlay", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            return "shown";
        }
    }
}
