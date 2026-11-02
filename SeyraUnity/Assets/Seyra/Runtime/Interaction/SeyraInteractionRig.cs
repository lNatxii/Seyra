using UnityEngine;

namespace Seyra.SceneTools
{
    // Marks SDK-owned geometry so scene services can skip the entire auxiliary subtree.
    [DisallowMultipleComponent]
    public sealed class SeyraInteractionRig : MonoBehaviour
    {
        Material highlightMaterial;
        public Material CreateHighlightMaterial()
        {
            if (highlightMaterial == null)
            {
                highlightMaterial = new Material(ManagedAppearance.LoadShader(AppearanceStyle.Transparent))
                    { name = "Seyra interaction highlight (owned)", hideFlags = HideFlags.DontSave };
                highlightMaterial.SetColor("_BaseColor", new Color(1, 1, 1, .25f));
            }
            return highlightMaterial;
        }
        void OnDestroy()
        {
            if (highlightMaterial == null) return;
            if (Application.isPlaying) Destroy(highlightMaterial); else DestroyImmediate(highlightMaterial);
            highlightMaterial = null;
        }
    }
}
