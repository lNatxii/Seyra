using System;
using System.Threading.Tasks;
using Seyra.Configuration;

namespace Seyra.Networking
{
    public interface IBridgeTransport : IDisposable
    {
        bool IsConnected { get; }
        event Action<string> Message;
        event Action<string> Status;
        void Start(string url);
        void Pump();
        Task SendAsync(string text);
        void ReconnectNow();
    }
    public sealed class BridgeSocketClient : IBridgeTransport
    {
        readonly IBridgeTransport transport;
        public BridgeSocketClient(SeyraConnectionSettings settings = null)
        {
            transport = settings?.IsGateway == true ? (IBridgeTransport)new PollingBridgeTransport(settings) : new WebSocketBridgeTransport();
        }
        public bool IsConnected => transport.IsConnected;
        public event Action<string> Message { add => transport.Message += value; remove => transport.Message -= value; }
        public event Action<string> Status { add => transport.Status += value; remove => transport.Status -= value; }
        public void Start(string url) => transport.Start(url);
        public void Pump() => transport.Pump();
        public Task SendAsync(string text) => transport.SendAsync(text);
        public void ReconnectNow() => transport.ReconnectNow();
        public void Dispose() => transport.Dispose();
    }
}
