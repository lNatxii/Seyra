using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Seyra.Networking;
using Seyra.SceneTools;
using UnityEditor;
using UnityEngine;

// Isolates test IDs, objects and WebSocket from an already-running chat/runtime.
public static class RunUnityIntegration
{
    public static Task<string> Run()=>RunCore(false,"node");
    public static Task<string> RunRoom(string nodeExecutable="node")=>RunCore(true,nodeExecutable);
    static async Task<string> RunCore(bool withRoom,string nodeExecutable)
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Run in Play Mode.");
        var runtime=UnityEngine.Object.FindFirstObjectByType<SeyraSceneRuntime>();
        if(runtime==null)throw new InvalidOperationException("A configured scene runtime is required.");
        var root=new GameObject("__SeyraIntegrationTests");
        root.transform.SetParent(runtime.transform,false);
        var storage=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/integration-slots-"+Guid.NewGuid().ToString("N")));
        var catalog=AssetDatabase.LoadAssetAtPath<Seyra.UI.Library.UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service=new SceneToolService(root.transform,storage,catalog);
        using var bridge=new BridgeSocketClient();
        async void Receive(string json)
        {
            try {await bridge.SendAsync(service.Dispatch(json));}
            catch(Exception error){UnityEngine.Debug.LogWarning("Integration fixture: "+error.Message);}
        }
        void Pump()=>bridge.Pump();
        bridge.Message+=Receive;
        EditorApplication.update+=Pump;
        bridge.Start("ws://127.0.0.1:4101");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backend/bridge"));
        var start=new ProcessStartInfo(nodeExecutable,"test-unity.mjs") {WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.EnvironmentVariables["SEYRA_UNITY_PORT"]="4101";
        Process process=null;
        GameObject testHead=null;
        var roomContext=UnityEngine.Object.FindFirstObjectByType<Seyra.Spatial.RoomContext>();
        var originalViewer=roomContext!=null?roomContext.Viewer:null;
        try
        {
            if(withRoom)
            {
                if(roomContext==null)throw new InvalidOperationException("Configure room support first.");
                Application.runInBackground=true;
                testHead=new GameObject("__IntegrationRoomViewer");testHead.transform.position=new Vector3(0,1.5f,0);
                var roomPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Office/Office00.prefab");
                await roomContext.LoadTestRoomAsync(roomPrefab,testHead.transform);
                if(roomContext.Snapshot.State!=Seyra.Spatial.RoomState.Ready)throw new Exception(roomContext.Diagnostic);
                start.EnvironmentVariables["SEYRA_TEST_ROOM"]="1";
            }
            process=Process.Start(start);
            var output=process.StandardOutput.ReadToEndAsync();
            var errors=process.StandardError.ReadToEndAsync();
            var deadline=DateTime.UtcNow.AddSeconds(45);
            while(!process.HasExited)
            {
                if(DateTime.UtcNow>deadline){process.Kill();throw new TimeoutException("Integration tests exceeded 45 seconds.");}
                await Task.Delay(50);
            }
            var text=await output;var error=await errors;
            if(process.ExitCode!=0)throw new Exception(text+"\n"+error);
            return text;
        }
        finally
        {
            EditorApplication.update-=Pump;
            if(process!=null){if(!process.HasExited)process.Kill();process.Dispose();}
            if(testHead!=null){roomContext.Configure(Meta.XR.MRUtilityKit.MRUK.Instance,originalViewer);UnityEngine.Object.DestroyImmediate(testHead);}
            UnityEngine.Object.DestroyImmediate(root);
            if(Directory.Exists(storage)){foreach(var file in Directory.EnumerateFiles(storage))File.Delete(file);Directory.Delete(storage);}
        }
    }
}
