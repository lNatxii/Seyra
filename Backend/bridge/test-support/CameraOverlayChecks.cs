using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Seyra.Cameras;
using Seyra.SceneTools;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CameraOverlayChecks
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var root = new GameObject("__CameraOverlayChecks");
        var meshes = new List<Mesh>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task Frame() { int frame = Time.frameCount; while (Time.frameCount <= frame + 1) await Task.Yield(); }
        try
        {
            var pose = Pose.identity;
            var grid = new CaptureDepthGrid { TanMin = new Vector2(-0.5f, -0.4f), TanMax = new Vector2(0.5f, 0.4f), HasCameraModel = true };
            var pixels = new List<Vector2>();
            void Fill(Vector3 normal, bool outliers = false)
            {
                pixels.Clear();
                Array.Clear(grid.Valid, 0, grid.Valid.Length);
                for (int row = 5; row < 25; row += 4) for (int column = 6; column < 36; column += 6)
                {
                    var viewport = CaptureDepthGrid.CellViewport(column, row);
                    var pixel = new Vector2(viewport.x * 1000, (1 - viewport.y) * 1000);
                    var ray = grid.PixelDirection(pixel.x, pixel.y, 1000, 1000, pose);
                    var point = ray * (Vector3.Dot(normal, new Vector3(0, 0, 1.5f)) / Vector3.Dot(normal, ray));
                    if (outliers && pixels.Count % 5 == 0) point += new Vector3(0, 0, 0.5f);
                    int index = row * CaptureDepthGrid.Columns + column;
                    grid.Points[index] = point; grid.Valid[index] = true; pixels.Add(pixel);
                }
            }
            Fill(Vector3.back);
            Check(CameraOverlayGeometry.TryFitPlane(grid, pose, 1000, 1000, pixels, out var plane), "Front plane must fit.");
            var rect = new Vector4(0, 0, 1000, 1000);
            Check(CameraOverlayGeometry.TryBuildMesh(grid, pose, 1000, 1000, rect, plane, out var mesh, out var size, out _) == "shown" &&
                Vector2.Distance(size, new Vector2(1.5f, 1.2f)) < 0.001f, "Front size must follow the camera tangents within 1 mm.");
            meshes.Add(mesh);
            var inclined = Quaternion.Euler(0, 30, 0) * Vector3.back;
            foreach (bool outliers in new[] { false, true })
            {
                Fill(inclined, outliers);
                Check(CameraOverlayGeometry.TryFitPlane(grid, pose, 1000, 1000, pixels, out plane) && plane.Plane == "fitted" &&
                    Vector3.Angle(plane.Normal, inclined) < 2, "Inclined plane must survive 20 percent outliers.");
                Check(CameraOverlayGeometry.TryBuildMesh(grid, pose, 1000, 1000, rect, plane, out mesh, out _, out _) == "shown", "Inclined mesh must build.");
                meshes.Add(mesh);
                foreach (var vertex in mesh.vertices)
                    Check(Mathf.Abs(Vector3.Dot(plane.Normal, vertex - plane.Point)) < 0.001f, "Each vertex must lie on the plane.");
            }
            Check(!CameraOverlayGeometry.TryFitPlane(grid, pose, 1000, 1000, new[] { pixels[0], pixels[1] }, out _), "Insufficient samples must return no_depth.");
            plane = new CameraOverlayGeometry.Surface { Point = new Vector3(0, 0, 5), Normal = Vector3.back };
            Check(CameraOverlayGeometry.TryBuildMesh(grid, pose, 1000, 1000, rect, plane, out _, out _, out _) == "too_large", "Large geometry must be rejected.");
            plane.Point = new Vector3(0, 0, 0.01f);
            Check(CameraOverlayGeometry.TryBuildMesh(grid, pose, 1000, 1000, rect, plane, out _, out _, out _) == "too_small", "Small geometry must be rejected.");

            var cardObject = new GameObject("Card"); cardObject.transform.SetParent(root.transform, false);
            var card = cardObject.AddComponent<ManagedPanel>();
            var overlayRoot = new GameObject("CameraOverlays"); overlayRoot.transform.SetParent(root.transform, false);
            var shader = Resources.Load<Shader>("Seyra/Camera/CameraOverlay");
            Check(shader != null, "Overlay shader must load from Resources.");
            CameraOverlay Show(string id) => CameraOverlay.Show(overlayRoot.transform, id, card, new Mesh(), new Texture2D(2, 2), shader);
            var first = Show("overlay_test");
            var oldMesh = first.GetComponent<MeshFilter>().sharedMesh;
            var oldMaterial = first.GetComponent<MeshRenderer>().sharedMaterial;
            var oldTexture = oldMaterial.mainTexture;
            Check(Show("overlay_test") == first, "Same overlay ID must replace the existing resources.");
            await Frame();
            Check(oldMesh == null && oldMaterial == null && oldTexture == null, "Replacement must release all previous resources.");
            oldMesh = first.GetComponent<MeshFilter>().sharedMesh;
            oldMaterial = first.GetComponent<MeshRenderer>().sharedMaterial;
            oldTexture = oldMaterial.mainTexture;
            for (int i = 0; i < 4; i++) Show("overlay_" + i);
            await Frame();
            Check(first == null && oldMesh == null && oldMaterial == null && oldTexture == null &&
                overlayRoot.GetComponentsInChildren<CameraOverlay>(true).Length == 4, "Fifth overlay must evict and release the oldest.");
            var remaining = overlayRoot.GetComponentsInChildren<CameraOverlay>(true);
            var releasedMeshes = new List<Mesh>(); var materials = new List<Material>(); var textures = new List<Texture>();
            foreach (var item in remaining)
            {
                releasedMeshes.Add(item.GetComponent<MeshFilter>().sharedMesh);
                var material = item.GetComponent<MeshRenderer>().sharedMaterial;
                materials.Add(material); textures.Add(material.mainTexture);
            }
            Object.Destroy(cardObject);
            await Frame(); await Frame();
            for (int i = 0; i < remaining.Length; i++)
                Check(remaining[i] == null && releasedMeshes[i] == null && materials[i] == null && textures[i] == null, "Destroyed cards must release overlay resources.");
            return "CameraOverlayChecks: geometry and lifecycle cases passed.";
        }
        finally
        {
            foreach (var mesh in meshes) if (mesh != null) Object.Destroy(mesh);
            Object.Destroy(root);
        }
    }
}
