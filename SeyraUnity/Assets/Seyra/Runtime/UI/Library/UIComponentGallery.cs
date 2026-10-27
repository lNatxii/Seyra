using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Local demonstration events. No application, permission, network or scene-object logic.</summary>
    public sealed class UIComponentGallery : MonoBehaviour
    {
        [SerializeField] TMP_Text status;
        [SerializeField] UIActionButton accept, cancel, interact, danger;
        [SerializeField] UIConfirmationPanel confirmation;
        [SerializeField] UIMetaDropdown dropdown;
        [SerializeField] UnityEngine.UI.Toggle toggle;
        [SerializeField] UnityEngine.UI.Slider slider;
        public string Status => status.text;
        void OnEnable()
        {
            accept.OnClick.AddListener(Accept); cancel.OnClick.AddListener(Cancel);
            interact.OnClick.AddListener(Interact); danger.OnClick.AddListener(Danger);
            confirmation.Accepted.AddListener(Accept); confirmation.Cancelled.AddListener(Cancel);
            dropdown.OnValueChanged.AddListener(Choose); toggle.onValueChanged.AddListener(Check); slider.onValueChanged.AddListener(Slide);
        }
        void OnDisable()
        {
            accept.OnClick.RemoveListener(Accept); cancel.OnClick.RemoveListener(Cancel);
            interact.OnClick.RemoveListener(Interact); danger.OnClick.RemoveListener(Danger);
            confirmation.Accepted.RemoveListener(Accept); confirmation.Cancelled.RemoveListener(Cancel);
            dropdown.OnValueChanged.RemoveListener(Choose); toggle.onValueChanged.RemoveListener(Check); slider.onValueChanged.RemoveListener(Slide);
        }
        void Accept() => status.text = "Aceptar · evento local recibido";
        void Cancel() => status.text = "Cancelar · evento local recibido";
        void Interact() => status.text = "Interactuar · evento local recibido";
        void Danger() => status.text = "Acción de peligro · demostración local";
        void Choose(int value) => status.text = "Selección · " + dropdown.Choices[value];
        void Check(bool value) => status.text = value ? "Opción activada" : "Opción desactivada";
        void Slide(float value) => status.text = "Valor · " + value.ToString("0.##");
        public void Configure(TMP_Text feedback, UIActionButton primary, UIActionButton secondary, UIActionButton action,
            UIActionButton destructive, UIConfirmationPanel dialog, UIMetaDropdown selection, UnityEngine.UI.Toggle boolean, UnityEngine.UI.Slider range)
        { status = feedback; accept = primary; cancel = secondary; interact = action; danger = destructive; confirmation = dialog; dropdown = selection; toggle = boolean; slider = range; }
    }
}
