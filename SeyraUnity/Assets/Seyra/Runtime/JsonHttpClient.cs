using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Seyra.Networking
{
    public sealed class OpenCodeHttpException : Exception
    {
        public long StatusCode { get; }
        public OpenCodeHttpException(long code, string message) : base(message) { StatusCode = code; }
    }
    // Called on Unity's main thread. No credentials are serialized or written to disk.
    public sealed class JsonHttpClient : IDisposable
    {
        readonly string baseUrl;
        readonly HashSet<UnityWebRequest> active = new HashSet<UnityWebRequest>();
        string password;
        bool disposed;

        public JsonHttpClient(string baseUrl) { this.baseUrl = baseUrl.TrimEnd('/'); }
        public void SetPassword(string value) { password = value; }

        public async Task<JObject> RequestAsync(string method, string path, JObject body, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(JsonHttpClient));
            token.ThrowIfCancellationRequested();
            using (var request = new UnityWebRequest(baseUrl + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                request.redirectLimit = 0;
                request.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(password))
                    request.SetRequestHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                active.Add(request);
                try
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (token.IsCancellationRequested || disposed) { request.Abort(); token.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(JsonHttpClient)); }
                        await Task.Yield();
                    }
                    token.ThrowIfCancellationRequested();
                    if (request.responseCode == 401)
#if UNITY_EDITOR
                        throw new InvalidOperationException("OpenCode ha rechazado la autenticación. Revisa OPENCODE_SERVER_PASSWORD en Backend/.env.local y pulsa Reconectar.");
#else
                        throw new InvalidOperationException("OpenCode ha rechazado la autenticación. Revisa las credenciales de conexión y pulsa Reconectar.");
#endif
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new OpenCodeHttpException(request.responseCode, $"OpenCode HTTP {request.responseCode}: {request.error}. Comprueba que el servidor esté iniciado.");
                    if (string.IsNullOrWhiteSpace(request.downloadHandler.text)) return new JObject();
                    try { return JObject.Parse(request.downloadHandler.text); }
                    catch (Exception error) { throw new InvalidOperationException("Respuesta de OpenCode incompatible: se esperaba JSON de la API v2.", error); }
                }
                finally { active.Remove(request); }
            }
        }

        public void Dispose()
        {
            disposed = true;
            password = null;
            foreach (var request in active) request.Abort();
        }
    }
}
