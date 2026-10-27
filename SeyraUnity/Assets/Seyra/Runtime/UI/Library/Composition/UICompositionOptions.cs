using System;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Nullable metrics preserve prefab preferences. Sizes are layout constraints, never positions.</summary>
    public sealed class UILayoutOptions
    {
        public float? MinWidth, MinHeight, PreferredWidth, PreferredHeight, FlexibleWidth, FlexibleHeight;
        public bool? IgnoreLayout;
        public static UILayoutOptions Weight(float width = 1, float height = 0) => new UILayoutOptions { FlexibleWidth = width, FlexibleHeight = height };
        internal void Validate()
        {
            foreach (var value in new[] { MinWidth, MinHeight, PreferredWidth, PreferredHeight, FlexibleWidth, FlexibleHeight })
                if (value.HasValue && (float.IsNaN(value.Value) || float.IsInfinity(value.Value) || value.Value < -1))
                    throw new ArgumentOutOfRangeException(nameof(UILayoutOptions), "Dimensions must be finite and >= -1 (automatic).");
        }
        internal void Apply(UnityEngine.UI.LayoutElement target)
        {
            if (MinWidth.HasValue) target.minWidth = MinWidth.Value;
            if (MinHeight.HasValue) target.minHeight = MinHeight.Value;
            if (PreferredWidth.HasValue) target.preferredWidth = PreferredWidth.Value;
            if (PreferredHeight.HasValue) target.preferredHeight = PreferredHeight.Value;
            if (FlexibleWidth.HasValue) target.flexibleWidth = FlexibleWidth.Value;
            if (FlexibleHeight.HasValue) target.flexibleHeight = FlexibleHeight.Value;
            if (IgnoreLayout.HasValue) target.ignoreLayout = IgnoreLayout.Value;
        }
    }

    /// <summary>A normalized rectangle inside the parent. Zero pixel offsets; no absolute-position API.</summary>
    public sealed class UIAnchorRect
    {
        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Pivot { get; }
        public UIAnchorRect(Vector2 min, Vector2 max, Vector2? pivot = null)
        { Min = min; Max = max; Pivot = pivot ?? (min + max) * .5f; Validate(); }
        internal void Validate()
        {
            foreach (var value in new[] { Min.x, Min.y, Max.x, Max.y, Pivot.x, Pivot.y })
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                    throw new ArgumentOutOfRangeException(nameof(UIAnchorRect), "Anchors and pivot must be between 0 and 1.");
            if (Max.x <= Min.x || Max.y <= Min.y) throw new ArgumentException("The rectangle must have positive area.");
        }
        internal void Apply(RectTransform target)
        { target.anchorMin = Min; target.anchorMax = Max; target.pivot = Pivot; target.offsetMin = target.offsetMax = Vector2.zero; }
    }

    public class UIElementOptions
    {
        public string Id, Name;
        public bool Active = true;
        public SeyraUITheme Theme;
        public UILayoutOptions Layout;
        // Null: parent LayoutGroup controls placement; otherwise fill parent via anchors.
        public UIAnchorRect Anchors;
    }
    public sealed class UIButtonOptions : UIElementOptions
    {
        public string Label;
        public bool Interactable = true;
        public Sprite Icon;
    }
    public sealed class UITextOptions : UIElementOptions
    {
        public UITextRole Role = UITextRole.Body;
        public UIInkRole Ink = UIInkRole.Text;
        public TMPro.TextAlignmentOptions Alignment = TMPro.TextAlignmentOptions.TopLeft;
    }
    public class UIPanelOptions : UIElementOptions
    {
        public string Title, Description;
        public bool ShowActions;
    }
    public sealed class UIConfirmationOptions : UIPanelOptions
    {
        public string Message, AcceptLabel = "Aceptar", CancelLabel = "Cancelar";
        public bool CloseOnDecision;
    }
    public class UIFieldOptions : UIElementOptions
    {
        public string Label, Help;
        public bool Interactable = true, HelpIsError;
    }
    public sealed class UIInputOptions : UIFieldOptions
    {
        public string Text, Placeholder;
        public int CharacterLimit = 4000;
        public bool ReadOnly;
    }
    public sealed class UIDropdownOptions : UIFieldOptions
    {
        public string[] Choices;
        public int Value;
    }
    public sealed class UIToggleOptions : UIFieldOptions { public bool IsOn; }
    public sealed class UISliderOptions : UIFieldOptions
    {
        public float Min = 0, Max = 100, Value = 50;
        public bool WholeNumbers;
        public string Format = "0.##", Suffix = "";
    }
    public sealed class UIFoldoutOptions : UIElementOptions { public string Title; public bool Expanded = true; }
    public sealed class UIListItemOptions : UIElementOptions
    {
        public string Title, Description, ActionLabel;
        public Sprite Icon;
        public bool ShowAction, Interactable = true;
    }
}
