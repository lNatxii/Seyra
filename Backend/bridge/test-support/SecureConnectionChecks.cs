using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Seyra.Configuration;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SecureConnectionChecks
{
    public static string Run(string root)
    {
        string paired = Path.Combine(root, ".seyra/pairing/test-phone.env");
        var settings = SeyraConnectionSettings.FromFile(paired, false);
        if (!settings.CanConnect || !settings.IsGateway) throw new Exception("Gateway settings failed.");
        string temporary = Path.Combine(root, "settings-test.env");
        File.WriteAllText(temporary, "SEYRA_GATEWAY_URL=https://127.0.0.1:8443\nSEYRA_DEVICE_TOKEN=fixture\nSEYRA_GATEWAY_CERT_SHA256=invalid\n");
        if (SeyraConnectionSettings.FromFile(temporary, false).ErrorCode != "CONNECTION_CONFIG_INVALID") throw new Exception("Invalid pin accepted.");
        File.WriteAllText(temporary, "");
        if (SeyraConnectionSettings.FromFile(temporary, false).ErrorCode != "CONNECTION_UNPAIRED") throw new Exception("Unpaired device accepted.");
        if (!SeyraConnectionSettings.FromFile(temporary, true).CanConnect) throw new Exception("Direct Editor mode failed.");
        using (var certificate = new X509Certificate2(File.ReadAllBytes(Path.Combine(root, "gateway.cer"))))
        {
            if (!new PinnedCertificate(settings.CertificateSha256).Matches(certificate) || new PinnedCertificate(new string('0',64)).Matches(certificate)) throw new Exception("Certificate matching failed.");
        }
        string editorConfig = Path.Combine(root, "editor-test.env");
        File.Copy(paired, editorConfig, true);
        File.AppendAllText(editorConfig, "\nOPENCODE_SERVER_PASSWORD=fixture-password\n");
        Environment.SetEnvironmentVariable("SEYRA_EDITOR_ENV_PATH", editorConfig);
        if (EditorSceneManager.GetActiveScene().isDirty) return "PASS: settings and pins. Active scene is dirty; scene preparation deferred.";
        EditorSceneManager.OpenScene("Assets/Scenes/MobileConnectionTest.unity");
        return "PASS: gateway, invalid pin, unpaired device, direct Editor and certificate match/mismatch. Prepared mobile scene with disabled test clients.";
    }
}
