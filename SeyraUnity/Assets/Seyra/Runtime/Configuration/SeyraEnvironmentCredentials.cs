using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Seyra.Configuration
{
    /// <summary>
    /// Unified environment credentials provider.
    /// In Editor / Play Mode: reads Backend/.env.local (or environment variables).
    /// In Player / Device build: reads Application.persistentDataPath/.env.local.
    /// Never packages or checks in secrets.
    /// </summary>
    public static class SeyraEnvironmentCredentials
    {
        public const string WitTokenEnvKey = "SEYRA_CLIENT_TOKEN";
        public const string OpenCodePasswordEnvKey = "OPENCODE_SERVER_PASSWORD";
        public const string OpenCodeUrlEnvKey = "OPENCODE_SERVER_URL";

        static string cachedPath;
        static DateTime lastReadUtc = DateTime.MinValue;
        static readonly Dictionary<string, string> cachedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string EnvFilePath
        {
            get
            {
#if UNITY_EDITOR
                var alternate = Environment.GetEnvironmentVariable("SEYRA_EDITOR_ENV_PATH");
                if (!string.IsNullOrWhiteSpace(alternate)) return Path.GetFullPath(alternate);
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend/.env.local"));
#else
                return Path.Combine(Application.persistentDataPath, ".env.local");
#endif
            }
        }

        public static bool EnvFileExists => File.Exists(EnvFilePath);

        public static string OpenCodeServerPassword => GetValue(OpenCodePasswordEnvKey);

        public static string WitClientToken => GetValue(WitTokenEnvKey);

        public static bool HasWitToken => !string.IsNullOrWhiteSpace(WitClientToken);

        public static string OpenCodeServerUrl
        {
            get
            {
                var url = GetValue(OpenCodeUrlEnvKey);
                return string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:4096" : url.TrimEnd('/');
            }
        }

#if UNITY_EDITOR
        static string PrefsPrefix => "Seyra.Wit." + Hash128.Compute(Application.dataPath) + ".";

        public static string PreferredMicrophone
        {
            get => UnityEditor.EditorPrefs.GetString(PrefsPrefix + "Microphone", "");
            set => UnityEditor.EditorPrefs.SetString(PrefsPrefix + "Microphone", value ?? "");
        }
#endif

        public static string GetValue(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            RefreshCacheIfStale();

            if (cachedValues.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;

            // Fallback to OS environment variable
            var envVal = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(envVal))
                return envVal.Trim();

            // Additional backward-compatible fallback for Wit token
            if (key == WitTokenEnvKey)
            {
                var legacy = Environment.GetEnvironmentVariable("SEYRA_WIT_CLIENT_TOKEN");
                if (!string.IsNullOrWhiteSpace(legacy)) return legacy.Trim();
            }

            return null;
        }

        public static void ForceReload()
        {
            lastReadUtc = DateTime.MinValue;
            RefreshCacheIfStale();
        }

        static void RefreshCacheIfStale()
        {
            var path = EnvFilePath;
            if (path != cachedPath || (DateTime.UtcNow - lastReadUtc).TotalSeconds > 3.0)
            {
                cachedPath = path;
                lastReadUtc = DateTime.UtcNow;
                cachedValues.Clear();

                if (!File.Exists(path)) return;

                try
                {
                    foreach (var rawLine in File.ReadLines(path))
                    {
                        var line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                        var eqIdx = line.IndexOf('=');
                        if (eqIdx <= 0) continue;

                        var key = line.Substring(0, eqIdx).Trim();
                        var val = line.Substring(eqIdx + 1).Trim();

                        if (val.Length >= 2 &&
                            ((val[0] == '"' && val[val.Length - 1] == '"') ||
                             (val[0] == '\'' && val[val.Length - 1] == '\'')))
                        {
                            val = val.Substring(1, val.Length - 2);
                        }

                        if (!string.IsNullOrEmpty(key))
                        {
                            cachedValues[key] = val;
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Seyra: could not read .env.local: " + error.Message);
                }
            }
        }

        public static string RedactToken(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var deviceToken = GetValue("SEYRA_DEVICE_TOKEN");
            if (!string.IsNullOrEmpty(deviceToken)) text = text.Replace(deviceToken, "[device token redacted]");
            var witToken = WitClientToken;
            if (!string.IsNullOrEmpty(witToken) && witToken.Length > 4)
                text = text.Replace(witToken, "[token oculto]");
            var opencodePass = OpenCodeServerPassword;
            if (!string.IsNullOrEmpty(opencodePass) && opencodePass.Length > 2)
                text = text.Replace(opencodePass, "[password oculta]");
            return text;
        }
    }
}
