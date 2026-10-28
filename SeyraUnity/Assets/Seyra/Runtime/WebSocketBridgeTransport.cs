using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Seyra.Networking
{
    // Network work runs off-thread; Pump delivers events on Unity's main thread.
    public sealed class WebSocketBridgeTransport : IBridgeTransport
    {
        const int MaxMessageBytes = 8388608;
        readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        ClientWebSocket socket;
        readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public event Action<string> Message;
        public event Action<string> Status;

        public void Start(string url)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "ws" || !uri.IsLoopback) throw new InvalidOperationException("Direct bridge requires loopback WS.");
            _ = RunAsync(uri);
        }
        public void ReconnectNow() { if (IsConnected) return; socket?.Abort(); try { wake.Release(); } catch (SemaphoreFullException) { } }
        public void Pump() { for (var i = 0; i < 64 && inbox.TryDequeue(out var action); i++) action(); }
        async Task RunAsync(Uri uri)
        {
            while (!lifetime.IsCancellationRequested)
            {
                using (var connection = new ClientWebSocket())
                {
                    socket = connection;
                    try
                    {
                        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                        {
                            connectTimeout.CancelAfter(3000);
                            await connection.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
                        }
                        inbox.Enqueue(() => Status?.Invoke("Runtime conectado"));
                        var buffer = new byte[4096];
                        while (connection.State == WebSocketState.Open && !lifetime.IsCancellationRequested)
                        {
                            using (var message = new MemoryStream())
                            {
                                WebSocketReceiveResult part;
                                do
                                {
                                    part = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token).ConfigureAwait(false);
                                    if (part.MessageType == WebSocketMessageType.Close) throw new IOException("The bridge closed the connection.");
                                    if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > MaxMessageBytes)
                                        throw new IOException("Invalid or oversized WebSocket message.");
                                    message.Write(buffer, 0, part.Count);
                                } while (!part.EndOfMessage);
                                if (inbox.Count >= 64) throw new IOException("Too many pending commands.");
                                var text = new UTF8Encoding(false, true).GetString(message.ToArray());
                                // Bind each queued command to its connection; never acknowledge on a replacement connection.
                                inbox.Enqueue(() => { if (ReferenceEquals(socket, connection) && IsConnected) Message?.Invoke(text); });
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        if (!lifetime.IsCancellationRequested)
                        {
                            var reason = error is WebSocketException ? "Puente no disponible" : error.Message;
                            inbox.Enqueue(() => Status?.Invoke(reason + " · reconectando"));
                        }
                    }
                    finally { connection.Abort(); }
                }
                if (!lifetime.IsCancellationRequested)
                {
                    try { await wake.WaitAsync(2000, lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
        }

        public async Task SendAsync(string text)
        {
            var connection = socket;
            await sendLock.WaitAsync(lifetime.Token);
            try
            {
                if (connection == null || connection.State != WebSocketState.Open || !ReferenceEquals(connection, socket))
                    throw new IOException("Runtime disconnected before confirmation.");
                await connection.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, lifetime.Token);
            }
            finally { sendLock.Release(); }
        }
        public void Dispose() { lifetime.Cancel(); socket?.Abort(); }
    }
}
