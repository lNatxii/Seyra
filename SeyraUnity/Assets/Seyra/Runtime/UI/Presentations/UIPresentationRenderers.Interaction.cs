using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    public static partial class UIPresentationRenderers
    {
        static void Checklist(UIPresentationView v, ChecklistData d)
        {
            var status = Text(v, "Comprobaciones", v.Body, UITextRole.Label);
            void Refresh() => status.text = $"{v.Values.Count(p => p.Value == "true")} / {d.items.Length} comprobaciones completadas";
            foreach (var group in d.items.GroupBy(i => i.group))
            {
                var parent = group.Key.Length > 0 ? Card(v, v.Body, group.Key) : v.Body;
                foreach (var item in group)
                {
                    v.Values[item.id] = item.completed ? "true" : "false";
                    var control = v.Factory.InstantiateToggle(parent, new UIToggleOptions { Label = item.title, Help = item.description, IsOn = item.completed }, on =>
                    { v.Values[item.id] = on ? "true" : "false"; Refresh(); v.Record("checklist.changed"); });
                    v.BindValue(item.id, value => ((UnityEngine.UI.Toggle)control.Control).SetIsOnWithoutNotify(value == "true"));
                }
            }
            Refresh();
            v.Restored += Refresh;
        }
        static void Diagnostic(UIPresentationView v, DiagnosticData d)
        {
            var pages = new List<RectTransform>();
            void Show(string node, bool record)
            {
                v.NodeId = node; v.SetIndex(System.Array.FindIndex(d.nodes, n => n.id == node), pages);
                if (record) v.Record("diagnostic.answer");
            }
            foreach (var node in d.nodes)
            {
                var page = Card(v, v.Body, node.title, node.description); pages.Add(page);
                foreach (var answer in node.answers)
                    v.Factory.InstantiateInteractButton(page, new UIButtonOptions { Label = answer.label }, () =>
                    { v.Values[node.id] = answer.id; Show(answer.nextNodeId, true); });
                if (node.answers.Length == 0) Text(v, "Fin de esta comprobación", page, UITextRole.Help);
            }
            v.Factory.InstantiateCancelButton(v.Actions, new UIButtonOptions { Label = "Reiniciar" }, () =>
            { v.Values.Clear(); Show(d.startNodeId, false); v.Record("diagnostic.restart"); });
            Show(d.startNodeId, false);
            v.RestoreNode = node => Show(node, false);
        }
        static void Choices(UIPresentationView v, ChoiceSelectorData d)
        {
            var controls = new Dictionary<string, UnityEngine.UI.Toggle>();
            var feedback = Text(v, "Selecciona una opción" + (d.multiple ? " o varias." : "."), v.Body, UITextRole.Help);
            foreach (var option in d.options)
            {
                var card = Column(v, v.Body, "ChoiceContainer");
                if (option.image != null) Photo(v, option.image, card);
                v.Values[option.id] = "false";
                var field = v.Factory.InstantiateToggle(card, new UIToggleOptions { Label = option.title, Help = option.description }, on =>
                {
                    if (on && !d.multiple)
                        foreach (var other in controls.Where(p => p.Key != option.id)) { other.Value.SetIsOnWithoutNotify(false); v.Values[other.Key] = "false"; }
                    v.Values[option.id] = on ? "true" : "false"; v.Record("selection.changed");
                });
                controls.Add(option.id, (UnityEngine.UI.Toggle)field.Control);
                v.BindValue(option.id, value => ((UnityEngine.UI.Toggle)field.Control).SetIsOnWithoutNotify(value == "true"));
            }
            v.Factory.InstantiateAcceptButton(v.Actions, new UIButtonOptions { Label = d.submitLabel }, () =>
            {
                var selected = v.Values.Where(p => p.Value == "true").Select(p => p.Key).ToArray();
                if (selected.Length == 0) { feedback.text = "Selecciona al menos una opción."; return; }
                v.Submission = new JObject { ["selectedIds"] = new JArray(selected) };
                feedback.text = "Selección registrada."; v.Record("selection.submit", true);
            });
            v.Restored += () => { if (v.Submitted) feedback.text = "Selección registrada."; };
        }
        static void Form(UIPresentationView v, ParameterFormData d)
        {
            var controls = new Dictionary<string, UIField>();
            var feedback = Text(v, "Completa los campos y pulsa " + d.submitLabel + ".", v.Body, UITextRole.Help);
            foreach (var field in d.fields)
            {
                var label = field.label + (field.unit.Length > 0 ? " (" + field.unit + ")" : "") + (field.required ? " *" : "");
                var value = field.value;
                if (value.Length == 0 && field.type == "toggle") value = "false";
                if (value.Length == 0 && field.type == "choice") value = field.choices[0];
                v.Values[field.id] = value;
                void Changed(string text) { v.Values[field.id] = text; v.Record("form.changed"); }
                UIField control;
                switch (field.type)
                {
                    case "toggle": control = v.Factory.InstantiateToggle(v.Body, new UIToggleOptions { Label = label, Help = field.help, IsOn = value == "true" }, on => Changed(on ? "true" : "false")); break;
                    case "choice":
                        // The native dropdown title displays its selected value, not a persistent field label.
                        var slot = Column(v, v.Body, "ChoiceFieldContainer");
                        Text(v, label, slot, UITextRole.Label);
                        control = v.Factory.InstantiateDropdown(slot, new UIDropdownOptions { Choices = field.choices, Value = System.Array.IndexOf(field.choices, value) }, i => Changed(field.choices[i]));
                        Text(v, field.help, slot, UITextRole.Help, UIInkRole.Muted);
                        break;
                    default:
                        control = v.Factory.InstantiateGrowingInputField(v.Body, new UIInputOptions { Label = label, Help = field.help, Text = value }, Changed);
                        // Keep drafts/exponents editable; the invariant numeric contract is checked on submit.
                        break;
                }
                controls.Add(field.id, control);
                v.BindValue(field.id, restored =>
                {
                    if (control.Control is UnityEngine.UI.Toggle toggle) toggle.SetIsOnWithoutNotify(restored == "true");
                    else if (field.type == "choice") control.GetComponent<UIMetaDropdown>().SetValue(System.Array.IndexOf(field.choices, restored), false);
                    else v.Factory.SetInputText(v.Factory.InstanceOf(control).Id, restored, false);
                });
            }
            v.Factory.InstantiateAcceptButton(v.Actions, new UIButtonOptions { Label = d.submitLabel }, () =>
            {
                var values = new JObject(); UIField firstInvalid = null;
                foreach (var field in d.fields)
                {
                    var valid = UIPresentationCodec.TryFieldValue(field, v.Values[field.id], out var value);
                    controls[field.id].SetHelp(valid ? field.help : "Revisa el valor" + (field.type == "number" ? $" ({Number(field.min)} … {Number(field.max)})" : "."), !valid);
                    if (!valid) { if (firstInvalid == null) firstInvalid = controls[field.id]; }
                    else values[field.id] = value;
                }
                if (firstInvalid != null)
                {
                    feedback.text = "Hay campos pendientes o valores fuera de rango.";
                    v.Factory.Focus(v.Factory.InstanceOf(firstInvalid).Id); v.Record("form.invalid"); return;
                }
                feedback.text = "Datos registrados."; v.Submission = values; v.Record("form.submit", true);
            });
            v.Restored += () => { if (v.Submitted) feedback.text = "Datos registrados."; };
        }
    }
}
