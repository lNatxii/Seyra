using System;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed partial class UIFactory
    {
        T FieldControl<T>(string id) where T : UnityEngine.UI.Selectable
        {
            var control = Required<UIField>(Get(id)).Control as T;
            if (control == null) throw new InvalidOperationException("The instance does not contain a " + typeof(T).Name + ".");
            return control;
        }
        public void SetText(string id, string text) => Required<TMP_Text>(Get(id)).text = text;
        public void SetButtonLabel(string id, string label) => Required<UIActionButton>(Get(id)).Label = label;
        public void SetButtonIcon(string id, Sprite icon) => Required<UIActionButton>(Get(id)).SetIcon(icon);
        public void SetPanelText(string id, string title, string description = null)
        {
            var panel = Required<UIPanelView>(Get(id)); panel.Title = title;
            if (description != null) panel.Description = description;
        }
        public void SetFieldHelp(string id, string help, bool error = false) => Required<UIField>(Get(id)).SetHelp(help, error);
        public void SetFieldLabel(string id, string label) => Required<UIField>(Get(id)).Label = label;
        public void SetInputText(string id, string text, bool notify = false)
        {
            var input = FieldControl<TMP_InputField>(id);
            ApplyInputText(input, text, notify);
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(Get(id).Root);
        }
        static void ApplyInputText(TMP_InputField input, string text, bool notify)
        {
            if (notify) { input.text = text; return; }
            // TMP invokes onValueChanged unconditionally in its Editor-only SetText branch.
            // Retain the exact event object and all persistent/runtime listeners around that call.
            var events = input.onValueChanged;
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            try { input.SetTextWithoutNotify(text); }
            finally { input.onValueChanged = events; }
        }
        public void SetToggleValue(string id, bool value, bool notify = false)
        {
            var toggle = FieldControl<UnityEngine.UI.Toggle>(id);
            if (notify) toggle.isOn = value; else toggle.SetIsOnWithoutNotify(value);
        }
        public void SetDropdownValue(string id, int index, bool notify = false)
        {
            Required<UIMetaDropdown>(Get(id)).SetValue(index, notify);
        }

        public void SetSliderValue(string id, float value, bool notify = false)
        {
            var slider = FieldControl<UnityEngine.UI.Slider>(id);
            if (!Finite(value) || value < slider.minValue || value > slider.maxValue) throw new ArgumentOutOfRangeException(nameof(value));
            if (notify) slider.value = value; else slider.SetValueWithoutNotify(value);
            Required<UISliderValue>(Get(id)).RefreshDisplay();
        }
        public void SetExpanded(string id, bool expanded, bool notify = false) => Required<UIFoldout>(Get(id)).SetExpanded(expanded, notify);
    }
}
