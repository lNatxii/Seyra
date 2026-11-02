using UnityEngine;

namespace Seyra.Cameras
{
    public struct CaptureObjectBox
    {
        const float MinimumDistance = 0.05f;
        const float MinimumHorizontalLength = 0.2f;
        const float MinimumCornerDot = 0.05f;
        const float ExtentPadding = 1.05f;
        const float MaximumApparentExtent = 3f;
        const float MinimumDepth = 0.03f;
        const float MaximumDepth = 0.30f;
        const float MaximumPitchDegrees = 75f;
        const float MinimumSize = 0.02f;
        const float MinimumHeightRatio = 0.35f;

        public Vector3 Center, Size;
        public Quaternion Rotation;
        public float Distance, PitchDegrees;

        public static bool TryEstimate(CaptureDepthGrid grid, Pose camera, int width, int height,
            float x1, float y1, float x2, float y2, Vector3 worldMedian, out CaptureObjectBox box)
        {
            box = default;
            if (!grid.HasCameraModel || x2 <= x1 || y2 <= y1) return false;
            var v = grid.PixelDirection((x1 + x2) / 2f, (y1 + y2) / 2f, width, height, camera).normalized;
            float distance = Vector3.Dot(worldMedian - camera.position, v);
            if (distance < MinimumDistance) return false;
            var forward = new Vector3(v.x, 0f, v.z);
            if (forward.magnitude < MinimumHorizontalLength)
            {
                forward = camera.rotation * Vector3.up;
                forward.y = 0f;
            }
            if (forward.magnitude < MinimumHorizontalLength) return false;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            var up = Vector3.Cross(v, right).normalized;
            float minRight = float.PositiveInfinity, maxRight = float.NegativeInfinity;
            float minUp = float.PositiveInfinity, maxUp = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                var direction = grid.PixelDirection((corner & 1) == 0 ? x1 : x2,
                    (corner & 2) == 0 ? y1 : y2, width, height, camera).normalized;
                float dot = Vector3.Dot(direction, v);
                if (dot < MinimumCornerDot) return false;
                var point = direction * (distance / dot);
                float horizontal = Vector3.Dot(point, right), vertical = Vector3.Dot(point, up);
                minRight = Mathf.Min(minRight, horizontal);
                maxRight = Mathf.Max(maxRight, horizontal);
                minUp = Mathf.Min(minUp, vertical);
                maxUp = Mathf.Max(maxUp, vertical);
            }
            float boxWidth = (maxRight - minRight) * ExtentPadding;
            float apparentHeight = (maxUp - minUp) * ExtentPadding;
            if (boxWidth > MaximumApparentExtent || apparentHeight > MaximumApparentExtent) return false;
            float depth = Mathf.Clamp(boxWidth, MinimumDepth, MaximumDepth);
            float pitch = Mathf.Min(Mathf.Asin(Mathf.Abs(v.y)), MaximumPitchDegrees * Mathf.Deg2Rad);
            float boxHeight = Mathf.Max(MinimumSize, MinimumHeightRatio * apparentHeight,
                (apparentHeight - depth * Mathf.Sin(pitch)) / Mathf.Cos(pitch));
            box = new CaptureObjectBox {
                Center = camera.position + v * (distance + depth / 2f),
                Size = new Vector3(Mathf.Max(boxWidth, MinimumSize), boxHeight, depth),
                Rotation = Quaternion.LookRotation(forward, Vector3.up),
                Distance = distance, PitchDegrees = pitch * Mathf.Rad2Deg
            };
            return true;
        }
    }
}
