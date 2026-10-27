using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Repairs missing TMP material references on owned instances, preserving valid theme materials.</summary>
    public static class UIAssetResources
    {
        public static int EnsureTextMaterials(GameObject root)
        {
            int restored = 0;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.font == null || label.fontSharedMaterial != null || label.font.material == null) continue;
                label.fontSharedMaterial = label.font.material;
                restored++;
            }
            return restored;
        }
    }
}
