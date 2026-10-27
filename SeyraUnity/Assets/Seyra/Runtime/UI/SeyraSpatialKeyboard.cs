using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Seyra.UI.Library;

namespace Seyra.UI
{
    /// <summary>
    /// In-world spatial virtual keyboard with poke and ray clickable keys, quick prompt chips,
    /// and full integration with TMP_InputField and Quest TouchScreenKeyboard.
    /// Enables complete conversation workflow in VR without PC keyboard or Inspector.
    /// </summary>
    public sealed class SeyraSpatialKeyboard : MonoBehaviour
    {
        [SerializeField] TMP_InputField targetInput;
        [SerializeField] Transform keysRoot;
        [SerializeField] Transform quickChipsRoot;
        [SerializeField] bool isShiftActive;

        public event Action SubmitRequested;

        public TMP_InputField TargetInput
        {
            get => targetInput;
            set => targetInput = value;
        }

        public bool IsShiftActive
        {
            get => isShiftActive;
            set
            {
                isShiftActive = value;
                RefreshKeyLabels();
            }
        }

        public void Configure(TMP_InputField input, Action onSubmit = null)
        {
            targetInput = input;
            if (onSubmit != null) SubmitRequested += onSubmit;
        }

        /// <summary>Appends or inserts text into the target input field.</summary>
        public void InsertText(string text)
        {
            if (targetInput == null || string.IsNullOrEmpty(text)) return;
            var current = targetInput.text ?? "";
            var pos = targetInput.caretPosition;
            if (pos < 0 || pos > current.Length) pos = current.Length;

            targetInput.text = current.Insert(pos, text);
            targetInput.caretPosition = pos + text.Length;
            targetInput.ForceLabelUpdate();
            targetInput.ActivateInputField();
        }

        /// <summary>Deletes one character before the caret position.</summary>
        public void Backspace()
        {
            if (targetInput == null) return;
            var current = targetInput.text ?? "";
            var pos = targetInput.caretPosition;
            if (pos <= 0 || current.Length == 0) return;
            if (pos > current.Length) pos = current.Length;

            targetInput.text = current.Remove(pos - 1, 1);
            targetInput.caretPosition = pos - 1;
            targetInput.ForceLabelUpdate();
            targetInput.ActivateInputField();
        }

        /// <summary>Clears the entire input text.</summary>
        public void ClearText()
        {
            if (targetInput == null) return;
            targetInput.text = "";
            targetInput.caretPosition = 0;
            targetInput.ForceLabelUpdate();
            targetInput.ActivateInputField();
        }

        /// <summary>Sets a prompt directly and optionally sends it.</summary>
        public void ApplyQuickPrompt(string promptText, bool autoSubmit = false)
        {
            if (targetInput == null || string.IsNullOrWhiteSpace(promptText)) return;
            targetInput.text = promptText.Trim();
            targetInput.caretPosition = targetInput.text.Length;
            targetInput.ForceLabelUpdate();
            targetInput.ActivateInputField();
            if (autoSubmit) SubmitRequested?.Invoke();
        }

        /// <summary>Triggers submission of the current input.</summary>
        public void Submit()
        {
            SubmitRequested?.Invoke();
        }

        public void ToggleShift()
        {
            IsShiftActive = !IsShiftActive;
        }

        void RefreshKeyLabels()
        {
            if (keysRoot == null) return;
            foreach (var key in keysRoot.GetComponentsInChildren<SeyraKeyButton>(true))
            {
                key.UpdateShift(isShiftActive);
            }
        }
    }
}
