using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

public static class PinningChecks
{
    public static async Task<string> Run(string certificatePath)
    {
        var expected = Hash(new X509Certificate2(File.ReadAllBytes(certificatePath)));
        int httpCallbacks = 0, socketCallbacks = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            bool valid = pass == 0;
            var pin = valid ? expected : new string('0', 64);
            using (var timeout = new CancellationTokenSource(10000))
            using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            {
                handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) => { httpCallbacks++; return Hash(cert) == pin; };
                using (var http = new HttpClient(handler))
                {
                    bool accepted = false;
                    try { using (var response = await http.GetAsync("https://127.0.0.1:18443", timeout.Token)) accepted = response.IsSuccessStatusCode; }
                    catch (HttpRequestException) { }
                    if (accepted != valid) throw new Exception("HTTPS pinning failed");
                }
            }
            using (var timeout = new CancellationTokenSource(10000))
            using (var socket = new ClientWebSocket())
            {
                socket.Options.Proxy = null;
                socket.Options.RemoteCertificateValidationCallback = (_, cert, chain, errors) => { socketCallbacks++; return Hash(cert) == pin; };
                bool accepted = false;
                string failure = "none";
                try { await socket.ConnectAsync(new Uri("wss://127.0.0.1:18443"), timeout.Token); accepted = socket.State == WebSocketState.Open; }
                catch (WebSocketException error) { failure = error.GetType().Name + ": " + error.Message + "; inner=" + error.InnerException?.GetType().Name + ": " + error.InnerException?.Message; }
                socket.Abort();
                if (accepted != valid) throw new Exception("WSS pinning failed; expected=" + valid + "; callbacks=" + socketCallbacks + "; " + failure);
            }
        }
        if (httpCallbacks < 2 || socketCallbacks < 2) throw new Exception("Certificate callbacks were not invoked");
        return "PASS: HTTPS and WSS accepted matching pin and rejected wrong pin; both callbacks invoked";
    }
    static string Hash(X509Certificate certificate)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(certificate.GetRawCertData())).Replace("-", "").ToLowerInvariant();
    }
}
