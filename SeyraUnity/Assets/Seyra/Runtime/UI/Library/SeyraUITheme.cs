using System;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public enum UITextRole { None, Title, Heading, Body, Label, Help }
    public enum UISurfaceRole { None, Background, Surface, Subtle, Field, Primary, Secondary, Danger, Border }
    public enum UIInkRole { Text, Muted, OnAction, Accent, Danger }
    public enum UISpacing { None, Small, Standard, Large }
    public enum UISizeRole { None, Control, Icon, ScrollRegion, DropdownPopup, DropdownContent, SliderHandle }

    [CreateAssetMenu(menuName = "Seyra/UI/Theme", fileName = "SeyraUITheme")]
    public sealed class SeyraUITheme : ScriptableObject
    {
        public TMP_FontAsset font;
        [Header("Semantic colors")]
        public Color background = new Color(.96f, .96f, .93f);
        public Color surface = Color.white;
        public Color subtle = new Color(.88f, .93f, .89f);
        public Color text = new Color(.12f, .18f, .19f);
        public Color muted = new Color(.29f, .36f, .36f);
        public Color primary = new Color(.10f, .34f, .29f);
        public Color secondary = new Color(.84f, .90f, .86f);
        public Color danger = new Color(.65f, .16f, .18f);
        public Color border = new Color(.32f, .43f, .40f);
        public Color focus = new Color(.15f, .48f, .40f);
        public Color disabled = new Color(.70f, .76f, .72f);
        [Header("Typography")]
        [Min(1)] public float titleSize = 30, headingSize = 22, bodySize = 18, labelSize = 18, helpSize = 16;
        [Header("Layout metrics (Canvas units, never positions)")]
        [Min(0)] public int smallSpacing = 8, standardSpacing = 12, largeSpacing = 24;
        [Min(1)] public float controlHeight = 48, iconSize = 24, scrollRegionHeight = 240;
        [Min(0)] public float borderThickness = 1, focusThickness = 2;
        public event Action Changed;
        void OnValidate() => Changed?.Invoke();

        public int Space(UISpacing role) => role == UISpacing.Large ? largeSpacing : role == UISpacing.Standard ? standardSpacing : role == UISpacing.Small ? smallSpacing : 0;
        public float FontSize(UITextRole role) => role == UITextRole.Title ? titleSize : role == UITextRole.Heading ? headingSize : role == UITextRole.Help ? helpSize : role == UITextRole.Label ? labelSize : bodySize;
        public Color Ink(UIInkRole role) => role == UIInkRole.Muted ? muted : role == UIInkRole.OnAction ? Color.white : role == UIInkRole.Accent ? primary : role == UIInkRole.Danger ? danger : text;
        public Color Surface(UISurfaceRole role) => role == UISurfaceRole.Background ? background : role == UISurfaceRole.Subtle ? subtle : role == UISurfaceRole.Primary ? primary : role == UISurfaceRole.Secondary ? secondary : role == UISurfaceRole.Danger ? danger : role == UISurfaceRole.Border ? border : surface;
    }
}
