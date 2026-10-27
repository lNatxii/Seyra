#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Seyra.Configuration
{
    // Editor only: delegates to unified SeyraEnvironmentCredentials
    public static class OpenCodeEditorCredentials
    {
        public static string ReadPassword(string backendDirectory = null)
        {
            return SeyraEnvironmentCredentials.OpenCodeServerPassword;
        }
    }
}
#endif

