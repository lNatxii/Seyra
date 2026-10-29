using System;
using System.IO;
using System.Linq;
using Seyra.Configuration;
using Seyra.Voice;
using UnityEngine;

public static class MuseDictationChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static string Run()
    {
        var pcm = PcmEncoding.FloatToPcm16(new[] { 0f, 1f, -1f, 2f }, 4);
        Check(pcm.SequenceEqual(new byte[] { 0, 0, 255, 127, 1, 128, 255, 127 }), "PCM16 conversion failed.");
        Check(PcmEncoding.Resample(new float[480], 480, 48000, 16000).Length == 160, "Resampling length failed.");

        string previous = Environment.GetEnvironmentVariable("SEYRA_EDITOR_ENV_PATH");
        string path = Path.GetTempFileName();
        GameObject host = null;
        try
        {
            Environment.SetEnvironmentVariable("SEYRA_EDITOR_ENV_PATH", path);
            File.WriteAllText(path, "SEYRA_GATEWAY_URL=https://127.0.0.1:8443\nSEYRA_DEVICE_TOKEN=fixture-device\nSEYRA_GATEWAY_CERT_SHA256=" + new string('a', 64) + "\n");
            SeyraEnvironmentCredentials.ForceReload();
            host = new GameObject("MuseProviderCheck");
            host.SetActive(false);
            var dictation = host.AddComponent<VoiceDictation>();
            dictation.ReloadEditorSettings();
            Check(dictation.Available && dictation.Provider == "Muse Voice Transcribe · pasarela", "Gateway must select Muse.");

            File.WriteAllText(path, "SEYRA_CLIENT_TOKEN=fixture-wit-token\n");
            SeyraEnvironmentCredentials.ForceReload();
            dictation.ReloadEditorSettings();
            Check(Application.isEditor && dictation.Available && dictation.Provider.StartsWith("Wit.ai", StringComparison.Ordinal), "Direct Editor must select Wit.");
            return "PASS: 3 groups (PCM16 little-endian/clipping, 48-to-16 kHz resampling, Muse/Wit provider selection). No microphone or network used.";
        }
        finally
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            Environment.SetEnvironmentVariable("SEYRA_EDITOR_ENV_PATH", previous);
            File.Delete(path);
            // Do not reload the restored path: it may refer to real private credentials.
        }
    }
}
