using System;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Samples;
using Oculus.Interaction.Surfaces;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;

namespace Seyra.Editor
{
    /// <summary>Reuses Meta's authored panel interactions, animation, materials and audio.</summary>
    public static class SeyraWindowGrabAuthoring
    {
        public const string SourcePath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Prefabs/PanelWithManipulators_UIExample.prefab";
        const string RailMeshPath = SeyraUILibraryBuilder.Root + "/Geometry/UIWindowGrabRail.asset";

        [MenuItem("Seyra/UI/Update Window Grab Frame")]
        public static void UpdateWindowPrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Update the prefab outside Play Mode.");
            var root = PrefabUtility.LoadPrefabContents(SeyraMetaUIAuthoring.WindowPath);
            try
            {
                var window = root.GetComponent<UIXRWindow>();
                Ensure(window);
                PrefabUtility.SaveAsPrefabAsset(root, SeyraMetaUIAuthoring.WindowPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Ensure(UIXRWindow window)
        {
            if (window == null || window.Canvas == null) throw new ArgumentException("The World Space window is missing.");
            var existing = window.GetComponentInChildren<UIMetaGrabFrame>(true);
            if (existing != null)
            {
                RemoveExampleCanvasInteractions(existing.gameObject);
                EnsureRailMesh(existing.gameObject);
                window.ConfigureGrab(existing); existing.RefreshGeometry();
                return;
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) throw new InvalidOperationException("The Meta SDK panel with manipulators is missing.");
            var frame = (GameObject)PrefabUtility.InstantiatePrefab(source, window.transform);
            // The Quick Actions cannot supply the UI example's animated rounded border.
            // Adapt the official self-contained prefab, keeping its visual assets by reference.
            PrefabUtility.UnpackPrefabInstance(frame, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            frame.name = "MetaGrabFrame";
            frame.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            frame.transform.localScale = Vector3.one;
            frame.AddComponent<UIMetaSource>().Configure(source);
            var visuals = frame.transform.Find("PanelVisuals").GetComponent<PanelWithManipulatorsBorderAffordanceController>();
            var grab = (Grabbable)new SerializedObject(visuals).FindProperty("_grabbale").objectReferenceValue;
            var panel = frame.transform.Find("PanelInteractable");
            var ray = panel.GetComponent<RayInteractable>();
            RemoveExampleCanvasInteractions(frame);

            // This iteration enables moving/rotating the whole window, not resizing its UI by stretching.
            foreach (var child in frame.GetComponentsInChildren<Transform>(true).Where(t => t.parent == frame.transform &&
                (t.name.StartsWith("Scaler", StringComparison.Ordinal) || t.name.StartsWith("Rotator", StringComparison.Ordinal))).ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            UnityEngine.Object.DestroyImmediate(frame.GetComponent<PanelSetup>());
            var sampleCanvas = panel.GetComponentInChildren<Canvas>(true);
            if (sampleCanvas != null) UnityEngine.Object.DestroyImmediate(sampleCanvas.gameObject);
            UnityEngine.Object.DestroyImmediate(panel.GetComponent<AnchoredWorldSpaceDistanceScaler>());
            panel.GetComponent<MeshRenderer>().enabled = false;
            panel.localPosition = Vector3.zero; panel.localRotation = Quaternion.identity; panel.localScale = Vector3.one;
            foreach (var collider in panel.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (var extra in frame.GetComponents<Grabbable>().Where(g => g != grab).ToArray()) UnityEngine.Object.DestroyImmediate(extra);
            foreach (var transformer in frame.GetComponents<MonoBehaviour>().Where(c => c is ITransformer).ToArray()) UnityEngine.Object.DestroyImmediate(transformer);
            var movement = frame.AddComponent<GrabFreeTransformer>(); // SDK defaults preserve scale, including two-hand grab.
            grab.InjectOptionalOneGrabTransformer(movement); grab.InjectOptionalTwoGrabTransformer(movement);
            grab.InjectOptionalTargetTransform(window.transform);
            grab.InjectOptionalThrowWhenUnselected(false);
            var body = frame.GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            grab.InjectOptionalRigidbody(body);
            frame.GetComponent<PanelHoverState>().grabbables = new System.Collections.Generic.List<Grabbable> { grab };
            ray.InjectOptionalPointableElement(grab);
            var plane = panel.gameObject.AddComponent<PlaneSurface>();
            plane.Facing = PlaneSurface.NormalFacing.Backward;
            var surface = panel.GetComponent<UnionClippedPlaneSurface>();
            var borders = new BoxCollider[4]; var clippers = new BoundsClipper[4];
            var names = new[] { "GrabTop", "GrabBottom", "GrabLeft", "GrabRight" };
            for (var i = 0; i < borders.Length; i++)
            {
                var border = new GameObject(names[i]); border.transform.SetParent(panel, false);
                borders[i] = border.AddComponent<BoxCollider>(); borders[i].isTrigger = true;
                clippers[i] = border.AddComponent<BoundsClipper>();
            }
            surface.InjectAllClippedPlaneSurface(plane, clippers);
            ray.InjectSurface(surface);
            // Native near grab discovers the four frame colliders through its existing Rigidbody.
            var corners = new[] { "AnchorTopLeft", "AnchorTopRight", "AnchorBottomLeft", "AnchorBottomRight" }
                .Select(name => frame.transform.Find(name)).ToArray();
            var adapter = frame.AddComponent<UIMetaGrabFrame>();
            adapter.Configure((RectTransform)window.Canvas.transform, grab, corners, borders, clippers, .08f, .05f);
            EnsureRailMesh(frame);
            window.ConfigureGrab(adapter);
        }

        static void RemoveExampleCanvasInteractions(GameObject frame)
        {
            // The example has a second ray/poke canvas rig beside its content. Removing
            // only the sample Canvas leaves its PointableCanvas/Surface references dangling.
            foreach (var pointable in frame.GetComponentsInChildren<PointableCanvas>(true))
                UnityEngine.Object.DestroyImmediate(pointable.gameObject);
        }

        static void EnsureRailMesh(GameObject frame)
        {
            // The SDK example ships its procedural rail without a persisted mesh.
            // Bake through the native generator once, sharing the mesh across windows.
            foreach (var generator in frame.GetComponentsInChildren<SkinnedRoundedBoxMesh>(true))
            {
                var fields = new SerializedObject(generator);
                var renderer = (SkinnedMeshRenderer)fields.FindProperty("_skinnedMeshRenderer").objectReferenceValue;
                SkinnedRoundedBoxMesh.GenerateMesh(
                    fields.FindProperty("_cornerSegmentCount").intValue,
                    fields.FindProperty("_borderRadius").floatValue,
                    fields.FindProperty("_cylinderFaceCount").intValue,
                    fields.FindProperty("_cylinderRadius").floatValue,
                    renderer,
                    (Transform)fields.FindProperty("_topLeft").objectReferenceValue,
                    (Transform)fields.FindProperty("_topRight").objectReferenceValue,
                    (Transform)fields.FindProperty("_bottomLeft").objectReferenceValue,
                    (Transform)fields.FindProperty("_bottomRight").objectReferenceValue);
                var generated = renderer.sharedMesh;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RailMeshPath);
                if (mesh == null)
                {
                    var directory = SeyraUILibraryBuilder.Root + "/Geometry";
                    if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder(SeyraUILibraryBuilder.Root, "Geometry");
                    AssetDatabase.CreateAsset(generated, RailMeshPath); mesh = generated;
                }
                else
                {
                    EditorUtility.CopySerialized(generated, mesh); EditorUtility.SetDirty(mesh);
                    UnityEngine.Object.DestroyImmediate(generated);
                }
                renderer.sharedMesh = mesh;
                generator.generateOnStart = false;
            }
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Mesh>(RailMeshPath));
        }
    }
}
