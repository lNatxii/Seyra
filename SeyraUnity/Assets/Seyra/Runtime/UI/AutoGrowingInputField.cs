using TMPro;
using UnityEngine;

namespace Seyra.UI
{
    /// <summary>Offers the input's wrapped text height to its parent layout, without moving any RectTransform.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TMP_InputField))]
    public sealed class AutoGrowingInputField : MonoBehaviour, UnityEngine.UI.ILayoutElement
    {
        [SerializeField] RectTransform availableHeightRoot;
        [SerializeField, Min(1)] int minimumLines = 2;
        [SerializeField, Min(1)] int maximumLines = 6;
        [SerializeField, Range(.1f, .5f)] float maximumHeightFraction = .3f;
        TMP_InputField input;
        RectTransform rect;
        float minimumHeight, measuredHeight;

        public float minWidth => 0;
        public float preferredWidth => 0;
        public float flexibleWidth => 1;
        public float minHeight => minimumHeight;
        public float preferredHeight => measuredHeight;
        public float flexibleHeight => 0;
        public int layoutPriority => 2;

        public void Configure(RectTransform heightRoot)
        {
            availableHeightRoot = heightRoot;
            Invalidate();
        }

        void OnEnable()
        {
            input = GetComponent<TMP_InputField>();
            rect = (RectTransform)transform;
            input.onValueChanged.AddListener(TextChanged);
            Invalidate();
        }

        void OnDisable()
        {
            if (input != null) input.onValueChanged.RemoveListener(TextChanged);
            Invalidate();
        }

        void OnValidate()
        {
            minimumLines = Mathf.Max(1, minimumLines);
            maximumLines = Mathf.Max(minimumLines, maximumLines);
            Invalidate();
        }

        void OnRectTransformDimensionsChange() => Invalidate();
        void TextChanged(string value) => Invalidate();

        void Invalidate()
        {
            if (rect == null) rect = transform as RectTransform;
            if (rect != null && !UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingLayout())
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(rect);
        }

        public void CalculateLayoutInputHorizontal() { }

        public void CalculateLayoutInputVertical()
        {
            if (input == null) input = GetComponent<TMP_InputField>();
            var text = input.textComponent;
            if (text == null || text.font == null) return;
            var group = GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            var padding = group != null ? group.padding : new RectOffset(16, 16, 12, 12);
            var line = text.GetPreferredValues("Mg", Mathf.Infinity, Mathf.Infinity).y;
            var twoLines = text.GetPreferredValues("Mg\nMg", Mathf.Infinity, Mathf.Infinity).y;
            var lineAdvance = Mathf.Max(line, twoLines - line);
            minimumHeight = line + lineAdvance * (minimumLines - 1) + padding.vertical;
            var limit = line + lineAdvance * (Mathf.Max(minimumLines, maximumLines) - 1) + padding.vertical;
            if (availableHeightRoot != null && availableHeightRoot.rect.height > 0)
                limit = Mathf.Min(limit, availableHeightRoot.rect.height * maximumHeightFraction);
            limit = Mathf.Max(minimumHeight, limit);
            var width = Mathf.Max(text.fontSize, ((RectTransform)transform).rect.width - padding.horizontal);
            var value = string.IsNullOrEmpty(input.text) && input.placeholder is TMP_Text hint ? hint.text : input.text;
            // Keep a trailing newline measurable, including pasted multiline text.
            var measured = value.EndsWith("\n", System.StringComparison.Ordinal) || value.EndsWith("\r", System.StringComparison.Ordinal) ? value + "M" : value;
            var contentHeight = text.GetPreferredValues(measured, width, Mathf.Infinity).y + padding.vertical;
            measuredHeight = Mathf.Clamp(contentHeight, minimumHeight, limit);
        }
    }
}
