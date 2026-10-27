using System;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed partial class UIFactory
    {
        /// <summary>IDs come from Catalog.Describe(); includes every installed UI Set prefab.</summary>
        public UIInstance InstantiateMetaComponent(string templateId, RectTransform parent = null, UIElementOptions options = null)
        {
            if (catalog == null || catalog.Get(templateId).Kind != UITemplateKind.MetaComponent)
                throw new ArgumentException("The ID must identify a Meta UI Set component.", nameof(templateId));
            return Instantiate(templateId, parent, options);
        }

        /// <summary>Anchor supplies the world pose. No implicit camera lookup or global rig creation.</summary>
        public UIXRWindow InstantiateXRWindow(Transform anchor, Vector2 canvasUnits, int themeIndex = 0)
            => UIXRWindow.Create(catalog, anchor, canvasUnits, themeIndex);
    }
}
