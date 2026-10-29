using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    public sealed partial class UIFactory
    {
        public RectTransform InstantiateContainer(RectTransform parent = null, UIElementOptions options = null) => Instantiate(UITemplateIds.Container, parent, options).ViewRoot;
        public RectTransform InstantiateRow(RectTransform parent = null, UIElementOptions options = null) => Instantiate(UITemplateIds.Row, parent, options).ViewRoot;
        public RectTransform InstantiateColumn(RectTransform parent = null, UIElementOptions options = null) => Instantiate(UITemplateIds.Column, parent, options).ViewRoot;
        public TMP_Text InstantiateText(string text, RectTransform parent = null, UITextOptions options = null)
        {
            options = options ?? new UITextOptions();
            return Create(UITemplateIds.Text, parent, options, i =>
            {
                var label = Required<TMP_Text>(i); label.text = text; label.alignment = options.Alignment;
                Required<UIStyle>(i).SetTypography(options.Role, options.Ink);
            }).View<TMP_Text>();
        }

        public UIActionButton InstantiateButton(RectTransform parent = null, UIButtonOptions options = null, UnityAction onClick = null, string templateId = UITemplateIds.Button)
            => CreateButton(templateId, parent, options, onClick, null);
        UIActionButton CreateButton(string templateId, RectTransform parent, UIButtonOptions options, UnityAction onClick, string defaultLabel)
        {
            options = options ?? new UIButtonOptions();
            return Create(templateId, parent, options, i =>
            {
                var button = Required<UIActionButton>(i);
                if (options.Label != null || defaultLabel != null) button.Label = options.Label ?? defaultLabel;
                button.Interactable = options.Interactable; button.SetIcon(options.Icon);
                if (onClick != null) button.OnClick.AddListener(onClick);
            }).View<UIActionButton>();
        }
        public UIActionButton InstantiateAcceptButton(RectTransform parent = null, UIButtonOptions options = null, UnityAction onClick = null)
            => CreateButton(UITemplateIds.PrimaryButton, parent, options, onClick, "Aceptar");
        public UIActionButton InstantiateCancelButton(RectTransform parent = null, UIButtonOptions options = null, UnityAction onClick = null)
            => CreateButton(UITemplateIds.SecondaryButton, parent, options, onClick, "Cancelar");
        public UIActionButton InstantiateInteractButton(RectTransform parent = null, UIButtonOptions options = null, UnityAction onClick = null)
            => CreateButton(UITemplateIds.Button, parent, options, onClick, "Interactuar");
        public UIActionButton InstantiateDangerButton(RectTransform parent = null, UIButtonOptions options = null, UnityAction onClick = null)
            => InstantiateButton(parent, options, onClick, UITemplateIds.DangerButton);

        static void ConfigurePanel(UIPanelView panel, UIPanelOptions options)
        {
            if (options.Title != null) panel.Title = options.Title;
            if (options.Description != null) panel.Description = options.Description;
            if (panel.Actions != null) panel.Actions.gameObject.SetActive(options.ShowActions);
        }
        public UIPanelView InstantiatePanel(RectTransform parent = null, UIPanelOptions options = null, bool titled = false)
        {
            options = options ?? new UIPanelOptions();
            return Create(titled ? UITemplateIds.TitledPanel : UITemplateIds.Panel, parent, options,
                i => ConfigurePanel(Required<UIPanelView>(i), options)).View<UIPanelView>();
        }
        public UIPanelView InstantiateTitledPanel(RectTransform parent = null, UIPanelOptions options = null) => InstantiatePanel(parent, options, true);
        public UIConfirmationPanel InstantiateConfirmationPanel(RectTransform parent = null, UIConfirmationOptions options = null, UnityAction onAccept = null, UnityAction onCancel = null)
        {
            options = options ?? new UIConfirmationOptions();
            return Create(UITemplateIds.Confirmation, parent, options, i =>
            {
                ConfigurePanel(Required<UIPanelView>(i), options);
                Required<UIPanelView>(i).Actions.gameObject.SetActive(true);
                var confirmation = Required<UIConfirmationPanel>(i);
                if (options.Message != null) confirmation.Message = options.Message;
                confirmation.CloseOnDecision = options.CloseOnDecision;
                confirmation.AcceptButton.Label = options.AcceptLabel; confirmation.CancelButton.Label = options.CancelLabel;
                if (onAccept != null) confirmation.Accepted.AddListener(onAccept);
                if (onCancel != null) confirmation.Cancelled.AddListener(onCancel);
            }).View<UIConfirmationPanel>();
        }
        static UIField ConfigureField(UIInstance instance, UIFieldOptions options)
        {
            var field = Required<UIField>(instance);
            if (options.Label != null) field.Label = options.Label;
            if (options.Help != null) field.SetHelp(options.Help, options.HelpIsError);
            field.Interactable = options.Interactable; return field;
        }
        public UIField InstantiateInputField(RectTransform parent = null, UIInputOptions options = null, UnityAction<string> onChanged = null, UnityAction<string> onSubmit = null, bool growing = false)
        {
            options = options ?? new UIInputOptions();
            if (options.CharacterLimit < 0) throw new ArgumentOutOfRangeException(nameof(options.CharacterLimit));
            return Create(growing ? UITemplateIds.GrowingInput : UITemplateIds.Input, parent, options, i =>
            {
                var input = (TMP_InputField)ConfigureField(i, options).Control;
                input.characterLimit = options.CharacterLimit; input.readOnly = options.ReadOnly;
                if (options.Text != null) ApplyInputText(input, options.Text, false);
                if (options.Placeholder != null && input.placeholder is TMP_Text placeholder) placeholder.text = options.Placeholder;
                var auto = input.GetComponent<AutoGrowingInputField>(); if (auto != null) auto.Configure(root);
                if (onChanged != null) input.onValueChanged.AddListener(onChanged);
                if (onSubmit != null) input.onSubmit.AddListener(onSubmit);
            }).View<UIField>();
        }
        public UIField InstantiateGrowingInputField(RectTransform parent = null, UIInputOptions options = null, UnityAction<string> onChanged = null, UnityAction<string> onSubmit = null)
            => InstantiateInputField(parent, options, onChanged, onSubmit, true);
        public UIField InstantiateDropdown(RectTransform parent = null, UIDropdownOptions options = null, UnityAction<int> onChanged = null)
        {
            options = options ?? new UIDropdownOptions();
            if (catalog == null) throw new InvalidOperationException("Configure the catalog before creating UI.");
            var count = options.Choices?.Length ?? catalog.Get(UITemplateIds.Dropdown).Prefab.GetComponent<UIMetaDropdown>().Choices.Count;
            if (count == 0 || options.Value < 0 || options.Value >= count || (options.Choices != null && options.Choices.Any(c => c == null)))
                throw new ArgumentException("Non-null choices and a valid index are required.");
            return Create(UITemplateIds.Dropdown, parent, options, i =>
            {
                ConfigureField(i, options);
                var dropdown = Required<UIMetaDropdown>(i);
                dropdown.SetChoices(options.Choices ?? dropdown.Choices.ToArray(), options.Value);
                if (onChanged != null) dropdown.OnValueChanged.AddListener(onChanged);
            }).View<UIField>();
        }
        public UIField InstantiateToggle(RectTransform parent = null, UIToggleOptions options = null, UnityAction<bool> onChanged = null)
        {
            options = options ?? new UIToggleOptions();
            return Create(UITemplateIds.Toggle, parent, options, i =>
            {
                var toggle = (UnityEngine.UI.Toggle)ConfigureField(i, options).Control;
                toggle.SetIsOnWithoutNotify(options.IsOn);
                if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            }).View<UIField>();
        }
        public UIField InstantiateSlider(RectTransform parent = null, UISliderOptions options = null, UnityAction<float> onChanged = null)
        {
            options = options ?? new UISliderOptions();
            if (!Finite(options.Min) || !Finite(options.Max) || !Finite(options.Value) || options.Max <= options.Min || options.Value < options.Min || options.Value > options.Max)
                throw new ArgumentException("The slider requires an increasing finite range and a value within that range.");
            _ = options.Value.ToString(options.Format, System.Globalization.CultureInfo.CurrentCulture);
            return Create(UITemplateIds.Slider, parent, options, i =>
            {
                var slider = (UnityEngine.UI.Slider)ConfigureField(i, options).Control;
                var events = slider.onValueChanged;
                slider.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();
                try
                {
                    slider.wholeNumbers = options.WholeNumbers; slider.minValue = options.Min; slider.maxValue = options.Max;
                    slider.SetValueWithoutNotify(options.Value);
                }
                finally { slider.onValueChanged = events; }
                Required<UISliderValue>(i).ConfigureDisplay(options.Format, options.Suffix);
                if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            }).View<UIField>();
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public UIFoldout InstantiateFoldout(RectTransform parent = null, UIFoldoutOptions options = null, UnityAction<bool> onChanged = null)
        {
            options = options ?? new UIFoldoutOptions();
            return Create(UITemplateIds.Foldout, parent, options, i =>
            {
                var foldout = Required<UIFoldout>(i);
                if (options.Title != null) foldout.Header.Label = options.Title;
                foldout.SetExpanded(options.Expanded, false);
                if (onChanged != null) foldout.ExpandedChanged.AddListener(onChanged);
            }).View<UIFoldout>();
        }
        public UnityEngine.UI.ScrollRect InstantiateScrollView(RectTransform parent = null, UIElementOptions options = null)
        {
            var scroll = Instantiate(UITemplateIds.ScrollView, parent, options).View<UnityEngine.UI.ScrollRect>();
            UIScrollIndicator.Ensure(scroll, catalog.DefaultTheme);
            return scroll;
        }
        public UIListItemView InstantiateListItem(RectTransform parent = null, UIListItemOptions options = null, UnityAction onAction = null)
        {
            options = options ?? new UIListItemOptions();
            return Create(UITemplateIds.ListItem, parent, options, i =>
            {
                var item = Required<UIListItemView>(i);
                if (options.Title != null) item.Panel.Title = options.Title;
                if (options.Description != null) item.Panel.Description = options.Description;
                item.SetIcon(options.Icon); item.ShowAction(options.ShowAction);
                if (options.ActionLabel != null) item.Action.Label = options.ActionLabel;
                item.Action.Interactable = options.Interactable;
                if (onAction != null) item.Action.OnClick.AddListener(onAction);
            }).View<UIListItemView>();
        }

    }
}
