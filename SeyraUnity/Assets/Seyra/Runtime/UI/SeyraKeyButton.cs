using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.UI
{
    /// <summary>
    /// Component attached to each virtual keyboard key button.
    /// Handles key click, shift state changes, and character insertion.
    /// </summary>
    public sealed class SeyraKeyButton : MonoBehaviour
    {
        [SerializeField] string baseCharacter;
        [SerializeField] string shiftCharacter;
        [SerializeField] TMP_Text label;
        [SerializeField] Button button;
        SeyraSpatialKeyboard keyboard;

        public void Setup(SeyraSpatialKeyboard host, string normal, string shifted = null)
        {
            keyboard = host;
            baseCharacter = normal;
            shiftCharacter = string.IsNullOrEmpty(shifted) ? normal.ToUpperInvariant() : shifted;
            if (label == null) label = GetComponentInChildren<TMP_Text>();
            if (button == null) button = GetComponent<Button>();
            if (label != null) label.text = baseCharacter;
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnClick);
            }
        }

        public void UpdateShift(bool shift)
        {
            if (label != null)
            {
                label.text = shift ? shiftCharacter : baseCharacter;
            }
        }

        void OnClick()
        {
            if (keyboard == null) return;
            var charToInsert = keyboard.IsShiftActive ? shiftCharacter : baseCharacter;
            keyboard.InsertText(charToInsert);
        }
    }
}
