using System;
using System.Globalization;
using System.Linq;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    /// <summary>Small adapters share semantic layout helpers and native prefab controls.</summary>
    public static partial class UIPresentationRenderers
    {
        public static void Render(UIPresentationView view, UIPresentationData data)
        {
            // Long descriptions belong in the scrollable region, keeping the heading and Close reachable.
            if (!(data is MediaCardData)) Text(view, data.description, view.Body);
            switch (data)
            {
                case MediaCardData d: MediaCard(view, d); break;
                case InfoPanelData d: Info(view, d); break;
                case TechnicalSheetData d: Technical(view, d); break;
                case GalleryData d: Gallery(view, d); break;
                case StepGuideData d: Guide(view, d); break;
                case DocumentViewerData d: Documents(view, d); break;
                case ComparisonData d: Comparison(view, d); break;
                case ChecklistData d: Checklist(view, d); break;
                case DiagnosticData d: Diagnostic(view, d); break;
                case ChoiceSelectorData d: Choices(view, d); break;
                case ParameterFormData d: Form(view, d); break;
                case DataViewData d: Data(view, d); break;
                case WorkPlanData d: Plan(view, d); break;
                default: throw new ArgumentException("Unregistered UI renderer.");
            }
        }
        static TMP_Text Text(UIPresentationView v, string text, RectTransform parent, UITextRole role = UITextRole.Body, UIInkRole ink = UIInkRole.Text)
            => string.IsNullOrEmpty(text) ? null : v.Factory.InstantiateText(text, parent, new UITextOptions { Role = role, Ink = ink });
        static RectTransform Column(UIPresentationView v, RectTransform parent, string name = null)
            => v.Factory.InstantiateColumn(parent, new UIElementOptions { Name = name, Layout = UILayoutOptions.Weight() });
        static RectTransform Card(UIPresentationView v, RectTransform parent, string title, string description = "")
        {
            var card = Column(v, parent, "PresentationCard");
            Text(v, title, card, UITextRole.Heading); Text(v, description, card); return card;
        }
        static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static void Property(UIPresentationView v, RectTransform parent, string label, string value)
        {
            var row = v.Factory.InstantiateContainer(parent);
            var adaptive = row.gameObject.AddComponent<UIAdaptiveRow>(); adaptive.Configure(v.Factory.Catalog.DefaultTheme);
            Text(v, label, Column(v, row, "LabelContainer"), UITextRole.Label);
            Text(v, value, Column(v, row, "ValueContainer"));
        }
        static void Info(UIPresentationView v, InfoPanelData d)
        {
            if (d.image != null) Photo(v, d.image, v.Body);
            foreach (var point in d.highlights) Text(v, "• " + point, v.Body);
        }
        static void Technical(UIPresentationView v, TechnicalSheetData d)
        {
            foreach (var p in d.properties) Property(v, v.Body, p.label, p.value + (p.unit.Length > 0 ? " " + p.unit : ""));
            foreach (var section in d.sections) Card(v, v.Body, section.title, section.description);
            foreach (var image in d.images) Photo(v, image, v.Body);
        }
        static void Comparison(UIPresentationView v, ComparisonData d)
        {
            foreach (var alternative in d.alternatives)
            {
                var card = Card(v, v.Body, alternative.title, alternative.description);
                for (var i = 0; i < d.criteria.Length; i++) Property(v, card, d.criteria[i], alternative.values[i]);
                foreach (var advantage in alternative.advantages) Text(v, "+ " + advantage, card);
                foreach (var disadvantage in alternative.disadvantages) Text(v, "− " + disadvantage, card);
            }
            if (d.recommendation.Length > 0) Card(v, v.Body, "Recomendación", d.recommendation);
        }
        static void Plan(UIPresentationView v, WorkPlanData d)
        {
            Text(v, $"{d.tasks.Count(t => t.status == "done")} / {d.tasks.Length} tareas completadas", v.Body, UITextRole.Label);
            foreach (var group in d.tasks.GroupBy(t => t.phase))
            {
                var parent = group.Key.Length > 0 ? Card(v, v.Body, group.Key) : v.Body;
                foreach (var task in group)
                {
                    var state = task.status == "done" ? "Completada" : task.status == "active" ? "En curso" : task.status == "blocked" ? "Bloqueada" : "Pendiente";
                    var card = Card(v, parent, task.title, task.description);
                    Text(v, state, card, UITextRole.Label);
                    if (task.dependsOn.Length > 0) Text(v, "Depende de: " + string.Join(", ", task.dependsOn.Select(id => d.tasks.First(t => t.id == id).title)), card, UITextRole.Help);
                }
            }
        }
        static void Data(UIPresentationView v, DataViewData d)
        {
            if (d.mode == "table")
            {
                foreach (var row in d.rows)
                {
                    var card = Column(v, v.Body, "DataRow");
                    for (var i = 0; i < d.columns.Length; i++) Property(v, card, d.columns[i], row.cells[i]);
                }
                return;
            }
            var magnitude = d.bars.Max(b => Math.Abs(b.value));
            foreach (var bar in d.bars)
            {
                Property(v, v.Body, bar.label, Number(bar.value) + " " + bar.unit);
                var track = v.Factory.InstantiateContainer(v.Body, new UIElementOptions { Name = "BarContainer", Layout = new UILayoutOptions { PreferredHeight = v.Factory.Catalog.DefaultTheme.iconSize } });
                var background = track.gameObject.AddComponent<UnityEngine.UI.Image>(); background.raycastTarget = false;
                track.gameObject.AddComponent<UIStyle>().Configure(v.Factory.Catalog.DefaultTheme, surface: UISurfaceRole.Subtle);
                var fill = new GameObject("Bar", typeof(RectTransform), typeof(UnityEngine.UI.Image)); fill.transform.SetParent(track, false);
                var rect = (RectTransform)fill.transform;
                // Shared zero at the center handles positive and negative values; dimensions derive from the data.
                var fraction = magnitude > 0 ? (float)(bar.value / magnitude) * .5f : 0;
                rect.anchorMin = new Vector2(.5f + Mathf.Min(0, fraction), 0); rect.anchorMax = new Vector2(.5f + Mathf.Max(0, fraction), 1);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                fill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                fill.AddComponent<UIStyle>().Configure(v.Factory.Catalog.DefaultTheme, surface: UISurfaceRole.Primary);
            }
        }
    }
}
