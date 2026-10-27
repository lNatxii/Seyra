using UnityEngine;

namespace Seyra.UI.Library
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class UIThemeScope : MonoBehaviour
    {
        [SerializeField] SeyraUITheme theme;
        bool pending;
        public SeyraUITheme Theme { get => theme; set { theme = value; Apply(); } }
        void OnEnable() => pending = true;
        void OnValidate() => pending = true;
        void LateUpdate() { if (pending) { pending = false; Apply(); } }
        public void Apply()
        {
            foreach (var style in GetComponentsInChildren<UIStyle>(true)) style.RefreshTheme();
        }
    }
}
