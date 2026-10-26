#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Seyra.Configuration
{
    // Editor only: the local backend file is never read or packaged by a player build.
    public static class OpenCodeEditorCredentials
    {
        public static string ReadPassword(string backendDirectory)
        {
            if (string.IsNullOrWhiteSpace(backendDirectory)) return null;
            try
            {
                string password = null;
                foreach (var line in File.ReadLines(Path.Combine(backendDirectory, ".env.local")))
                {
                    var entry = line.Trim();
                    if (entry.Length == 0 || entry.StartsWith("#", StringComparison.Ordinal)) continue;
                    var separator = entry.IndexOf('=');
                    if (separator < 0 || entry.Substring(0, separator).Trim() != "OPENCODE_SERVER_PASSWORD") continue;
                    var value = entry.Substring(separator + 1).Trim();
                    // Match Backend/start.ps1: strip paired quotes, preserve literal content.
                    if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                        (value[0] == '\'' && value[value.Length - 1] == '\'')))
                        value = value.Substring(1, value.Length - 2);
                    password = value;
                }
                return string.IsNullOrEmpty(password) ? null : password;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException)
            {
                // Never log file contents, credentials, or exception messages containing them.
                Debug.LogWarning("Seyra: no se pudo leer Backend/.env.local. Comprueba el archivo y pulsa Reconectar.");
                return null;
            }
        }
    }
}
#endif
