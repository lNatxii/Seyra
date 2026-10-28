using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Seyra.Configuration;

public static class PinningChecks
{
    public static async Task<string> Run(string certificatePath)
    {
        using (var certificate = new X509Certificate2(File.ReadAllBytes(certificatePath)))
        {
            string result = await ConnectionDiagnostics.RunTlsProbe("https://127.0.0.1:18443", PinnedCertificate.Hash(certificate));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(certificatePath), "editor-results.txt"), result);
            foreach (string line in result.Split('\n'))
            {
                if (line.StartsWith("WSS per-socket")) continue;
                bool expected = line.Contains(" matching:");
                if (!line.Contains("accepted=" + expected) || line.Contains("callbacks=0;"))
                    throw new Exception("Pinning check failed: " + line);
            }
            return "PASS: HTTPS, polling fixture and global WSS accepted matching pin and rejected wrong pin; callbacks invoked.\n" + result;
        }
    }
}
