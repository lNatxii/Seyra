using System;
using Seyra.Networking;
using Seyra.Configuration;
using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class SeyraSceneRuntime : MonoBehaviour
    {
        [SerializeField] Transform generatedRoot;
        [SerializeField] Seyra.UI.Library.UIPrefabCatalog uiCatalog;
        [SerializeField] string bridgeUrl = "ws://127.0.0.1:4100";
        static SeyraSceneRuntime owner;
        BridgeSocketClient bridge;
        public SceneToolService Tools { get; private set; }
        public bool IsBridgeConnected => bridge != null && bridge.IsConnected;
        public string BridgeStatus { get; private set; } = "Disconnected";

        void OnEnable()
        {
            if(owner!=null && owner!=this) { Debug.LogWarning("Seyra: a runtime is already active in another scene.");enabled=false;return; }
            if(generatedRoot==null) {Debug.LogError("Seyra: assign the Generated container in SeyraSceneRuntime.");enabled=false;return;}
            var settings = SeyraConnectionSettings.Load();
            if (!settings.CanConnect) { BridgeStatus = ConnectionErrors.Describe(null, settings); return; }
            if (!settings.IsGateway && (!Uri.TryCreate(bridgeUrl, UriKind.Absolute, out var uri) || uri.Scheme != "ws" || !uri.IsLoopback))
            { BridgeStatus = "Invalid connection configuration"; return; }
            owner=this;
            if (Tools == null) Tools=new SceneToolService(generatedRoot,panelCatalog:uiCatalog);
            bridge=new BridgeSocketClient(settings);bridge.Message+=Receive;
            bridge.Status+=status=>BridgeStatus=status;
            bridge.Start(settings.IsGateway ? settings.BridgeUrl : bridgeUrl);
        }
        public void ReconnectNow(bool reloadConfiguration = false)
        {
            if (reloadConfiguration) { SeyraEnvironmentCredentials.ForceReload(); bridge?.Dispose(); bridge = null; }
            if (bridge == null) { if (enabled) OnEnable(); return; }
            bridge.ReconnectNow();
        }
        void OnApplicationPause(bool paused) { if (!paused && !IsBridgeConnected) ReconnectNow(); }
        void Update()=>bridge?.Pump();
        async void Receive(string json)
        {
            var connection=bridge;
            try { var result=Tools.Dispatch(json);await connection.SendAsync(result); }
            catch(Exception error) { if(this!=null && isActiveAndEnabled)Debug.LogWarning("Seyra: could not confirm the command: "+error.Message); }
        }
        void OnDisable()
        {
            bridge?.Dispose();bridge=null;Tools?.Dispose();Tools=null;
            if(owner==this)owner=null;
            BridgeStatus="Disconnected";
        }
    }
}
