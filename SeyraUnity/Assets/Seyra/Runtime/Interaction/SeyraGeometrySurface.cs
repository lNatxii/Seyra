using System.Linq;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed class SeyraGeometrySurface : MonoBehaviour, ISurface
    {
        MeshFilter[] meshes = new MeshFilter[0];
        public Transform Transform => transform;
        public void Configure(Transform root)
        {
            meshes = root.GetComponentsInChildren<MeshFilter>(true).Where(mesh =>
                mesh.GetComponentInParent<SeyraInteractionRig>(true) == null &&
                mesh.GetComponentInParent<ManagedPanel>(true) == null).ToArray();
        }
        static Bounds MeshBounds(MeshFilter mesh)
        {
            var bounds = mesh.sharedMesh.bounds;
            var scale = mesh.transform.lossyScale;
            bounds.size = Vector3.Max(bounds.size, new Vector3(.08f / Mathf.Abs(scale.x), .08f / Mathf.Abs(scale.y), .08f / Mathf.Abs(scale.z)));
            return bounds;
        }
        public bool Raycast(in Ray ray, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default; bool found = false;
            float nearest = maxDistance > 0 ? maxDistance : float.PositiveInfinity;
            foreach (var mesh in meshes)
            {
                if (mesh == null || !mesh.gameObject.activeInHierarchy || mesh.sharedMesh == null) continue;
                var matrix = mesh.transform.worldToLocalMatrix;
                var localRay = new Ray(matrix.MultiplyPoint3x4(ray.origin), matrix.MultiplyVector(ray.direction));
                var bounds = MeshBounds(mesh);
                if (!bounds.IntersectRay(localRay, out float distance)) continue;
                var point = mesh.transform.TransformPoint(localRay.GetPoint(distance));
                float worldDistance = Vector3.Distance(ray.origin, point);
                if (worldDistance > nearest) continue;
                var relative = point - mesh.transform.TransformPoint(bounds.center);
                hit = new SurfaceHit { Point = point, Normal = relative.normalized, Distance = worldDistance };
                nearest = worldDistance; found = true;
            }
            return found;
        }
        public bool ClosestSurfacePoint(in Vector3 point, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default; bool found = false;
            float nearest = maxDistance > 0 ? maxDistance : float.PositiveInfinity;
            foreach (var mesh in meshes)
            {
                if (mesh == null || !mesh.gameObject.activeInHierarchy || mesh.sharedMesh == null) continue;
                var closest = mesh.transform.TransformPoint(MeshBounds(mesh).ClosestPoint(mesh.transform.InverseTransformPoint(point)));
                float distance = Vector3.Distance(point, closest);
                if (distance > nearest) continue;
                hit = new SurfaceHit { Point = closest, Normal = (point - closest).normalized, Distance = distance };
                nearest = distance; found = true;
            }
            return found;
        }
    }
}
