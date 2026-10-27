using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    /// <summary>Reflows label/value containers vertically when readable columns no longer fit.</summary>
    public sealed class UIAdaptiveRow : UnityEngine.UI.HorizontalOrVerticalLayoutGroup
    {
        [SerializeField] SeyraUITheme theme;
        bool Stacked => theme != null && rectTransform.rect.width < theme.controlHeight * 3 * rectChildren.Count;
        public void Configure(SeyraUITheme metrics)
        {
            theme = metrics;
            childControlWidth = childControlHeight = true;
            childForceExpandWidth = true; childForceExpandHeight = false;
            spacing = metrics != null ? metrics.smallSpacing : 0;
            SetDirty();
        }
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); CalcAlongAxis(0, Stacked); }
        public override void CalculateLayoutInputVertical() => CalcAlongAxis(1, Stacked);
        public override void SetLayoutHorizontal() => SetChildrenAlongAxis(0, Stacked);
        public override void SetLayoutVertical() => SetChildrenAlongAxis(1, Stacked);
    }
}
