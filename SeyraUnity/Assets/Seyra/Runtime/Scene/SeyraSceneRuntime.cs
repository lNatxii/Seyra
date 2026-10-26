using System;
using Seyra.Networking;
using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class SeyraSceneRuntime : MonoBehaviour
    {
        [SerializeField] Transform generatedRoot;
        [SerializeField] string bridgeUrl = "ws://127.0.0.1:4100";
        static SeyraSceneRuntime owner;
        BridgeSocketClient bridge;
        public SceneToolService Tools { get; private set; }
        public bool IsBridgeConnected => bridge != null && bridge.IsConnected;
        public string BridgeStatus { get; private set; } = "Desconectado";

        void OnEnable()
        {
            if(owner!=null && owner!=this) { Debug.LogWarning("Seyra: ya hay un runtime activo en otra escena.");enabled=false;return; }
            if(generatedRoot==null) {Debug.LogError("Seyra: asigna el contenedor Generated en SeyraSceneRuntime.");enabled=false;return;}
            if(!Uri.TryCreate(bridgeUrl,UriKind.Absolute,out var uri) || uri.Scheme!="ws" || !uri.IsLoopback)
            {Debug.LogError("Seyra: esta primera integración requiere un bridge WebSocket local.");enabled=false;return;}
            owner=this;
            Tools=new SceneToolService(generatedRoot);
            bridge=new BridgeSocketClient();bridge.Message+=Receive;
            bridge.Status+=status=>BridgeStatus=status;
            bridge.Start(bridgeUrl);
        }
        void Update()=>bridge?.Pump();
        async void Receive(string json)
        {
            var connection=bridge;
            try { var result=Tools.Dispatch(json);await connection.SendAsync(result); }
            catch(Exception error) { if(this!=null && isActiveAndEnabled)Debug.LogWarning("Seyra: no se pudo confirmar la orden: "+error.Message); }
        }
        void OnDisable()
        {
            bridge?.Dispose();bridge=null;Tools=null;
            if(owner==this)owner=null;
            BridgeStatus="Desconectado";
        }
    }
}
