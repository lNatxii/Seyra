using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Seyra.UI.Library
{
    /// <summary>Dynamic choices on Meta's animated dropdown; owns bindings instead of the fixed sample list.</summary>
    public sealed class UIMetaDropdown : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Toggle header;
        [SerializeField] UnityEngine.UI.Toggle optionPrefab;
        [SerializeField] RectTransform optionsRoot;
        [SerializeField] TMP_Text caption;
        [SerializeField] string[] choices = { "Opción 1", "Opción 2", "Opción 3" };
        [SerializeField] int value;
        [SerializeField] UnityEvent<int> changed = new UnityEvent<int>();
        readonly List<UnityEngine.UI.Toggle> options = new List<UnityEngine.UI.Toggle>();
        bool initialized;
        public int Value => value;
        public IReadOnlyList<string> Choices => choices;
        public UnityEvent<int> OnValueChanged => changed;
        public UnityEngine.UI.Toggle Header => header;
        public void Configure(UnityEngine.UI.Toggle trigger, UnityEngine.UI.Toggle prototype, RectTransform list, TMP_Text title)
        { header = trigger; optionPrefab = prototype; optionsRoot = list; caption = title; }
        void OnEnable() { if (!initialized) SetChoices(choices, value); }
        public void SetChoices(string[] labels, int selected = 0)
        {
            if (labels == null || labels.Length == 0 || labels.Any(x => x == null) || selected < 0 || selected >= labels.Length)
                throw new ArgumentException("Choices and a valid index are required.");
            foreach (var option in optionsRoot.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).Where(t => t != optionPrefab && t.transform.parent == optionsRoot))
            {
                option.gameObject.SetActive(false);
                option.transform.SetParent(null, false);
                if (Application.isPlaying) Destroy(option.gameObject); else DestroyImmediate(option.gameObject);
            }
            options.Clear(); choices = (string[])labels.Clone();
            for (var i = 0; i < choices.Length; i++)
            {
                var index = i;
                var option = Instantiate(optionPrefab, optionsRoot, false);
                option.group = null;
                var title = option.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "Title");
                title.text = choices[i];
                option.SetIsOnWithoutNotify(i == selected);
                option.onValueChanged.AddListener(on =>
                {
                    // Clicking the selected Toggle must not leave the list with no selected row.
                    if (!on && index != value) return;
                    SetValue(index, true);
                    header.isOn = false;
                });
                options.Add(option); option.gameObject.SetActive(true);
            }
            initialized = true; SetValue(selected, false);
            header.GetComponentInParent<Oculus.Interaction.UIThemeManager>()?.ApplyCurrentTheme();
            UIAssetResources.EnsureTextMaterials(optionsRoot.gameObject);
        }
        public void SetValue(int index, bool notify = false)
        {
            if (index < 0 || index >= choices.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var different = value != index; value = index;
            caption.text = choices[index];
            for (var i = 0; i < options.Count; i++) options[i].SetIsOnWithoutNotify(i == index);
            if (notify && different) changed.Invoke(index);
        }
    }
}
