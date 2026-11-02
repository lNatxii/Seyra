using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seyra.Cameras
{
    public sealed class CaptureDepthGrid
    {
        public const int Columns = 40, Rows = 30;
        public const float MaxDistance = 6f;
        public const bool ImageRowsFromTop = true; // Confirm on Quest in step 2.
        public readonly Vector3[] Points = new Vector3[Columns * Rows];
        public readonly bool[] Valid = new bool[Columns * Rows];
        public int Hits;
        public Vector2 TanMin, TanMax;
        public bool HasCameraModel;

        // World direction through a photo pixel measured from the top left, not normalized.
        public Vector3 PixelDirection(float pixelX, float pixelY, int width, int height, Pose pose)
        {
            float x = pixelX / width;
            float y = ImageRowsFromTop ? 1f - pixelY / height : pixelY / height;
            return pose.rotation * new Vector3(Mathf.Lerp(TanMin.x, TanMax.x, x),
                Mathf.Lerp(TanMin.y, TanMax.y, y), 1f);
        }

        public static Vector2 CellViewport(int column, int row) =>
            new Vector2((column + 0.5f) / Columns, (row + 0.5f) / Rows);

        public bool TryGet(float pixelX, float pixelY, int width, int height, out Vector3 point)
        {
            point = default;
            if (width <= 0 || height <= 0 || float.IsNaN(pixelX) || float.IsNaN(pixelY) ||
                pixelX < 0 || pixelY < 0 || pixelX >= width || pixelY >= height) return false;
            float y = ImageRowsFromTop ? 1f - pixelY / height : pixelY / height;
            int column = Mathf.Clamp(Mathf.FloorToInt(pixelX / width * Columns), 0, Columns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(y * Rows), 0, Rows - 1);
            int index = row * Columns + column;
            if (!Valid[index]) return false;
            point = Points[index];
            return true;
        }

        public static bool TryMedian(IReadOnlyList<Vector3> samples, int requested, out Vector3 median)
        {
            median = default;
            if (samples == null || requested <= 0 || samples.Count < 3 || samples.Count < requested * 0.4f)
                return false;
            var x = new float[samples.Count];
            var y = new float[samples.Count];
            var z = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                x[i] = samples[i].x;
                y[i] = samples[i].y;
                z[i] = samples[i].z;
            }
            median = new Vector3(Middle(x), Middle(y), Middle(z));
            return true;
        }

        static float Middle(float[] values)
        {
            Array.Sort(values);
            int middle = values.Length / 2;
            return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5f : values[middle];
        }
    }
}
