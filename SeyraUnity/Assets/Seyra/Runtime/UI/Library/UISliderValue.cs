using System.Globalization;
using TMPro;
using UnityEngine;

namespace Seyra.UI.Library
{
    public sealed class UISliderValue : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Slider slider;
        [SerializeField] TMP_Text label;
        [SerializeField] string format = "0.##", suffix = "";
        public UnityEngine.UI.Slider Slider => slider;
        void OnEnable() { if (slider != null) { slider.onValueChanged.AddListener(Refresh); Refresh(slider.value); } }
        void OnDisable() { if (slider != null) slider.onValueChanged.RemoveListener(Refresh); }
        void Refresh(float value) { if (label != null) label.text = value.ToString(format, CultureInfo.CurrentCulture) + suffix; }
        public void ConfigureDisplay(string numberFormat, string unit = "")
        {
            _ = 0f.ToString(numberFormat, CultureInfo.CurrentCulture);
            format = numberFormat; suffix = unit; RefreshDisplay();
        }
        public void RefreshDisplay() { if (slider != null) Refresh(slider.value); }
        public void Configure(UnityEngine.UI.Slider control, TMP_Text value) { slider = control; label = value; Refresh(slider.value); }
    }
}
