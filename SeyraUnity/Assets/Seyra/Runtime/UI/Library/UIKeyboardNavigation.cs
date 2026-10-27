using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Opt-in authoring default for semantic adapters; retains explicit navigation links.</summary>
    public static class UIKeyboardNavigation
    {
        public static int EnableAutomatic(GameObject root)
        {
            int changed = 0;
            foreach (var control in root.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
            {
                var navigation = control.navigation;
                if (navigation.mode != UnityEngine.UI.Navigation.Mode.None) continue;
                navigation.mode = UnityEngine.UI.Navigation.Mode.Automatic;
                control.navigation = navigation; changed++;
            }
            return changed;
        }
    }
}
