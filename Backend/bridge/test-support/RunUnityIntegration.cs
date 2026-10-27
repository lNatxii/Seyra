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
    public static async Task<string> Run()
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
        var start=new ProcessStartInfo("node","test-unity.mjs") {WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.EnvironmentVariables["SEYRA_UNITY_PORT"]="4101";
        Process process=null;
        try
        {
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
            UnityEngine.Object.DestroyImmediate(root);
            if(Directory.Exists(storage)){foreach(var file in Directory.EnumerateFiles(storage))File.Delete(file);Directory.Delete(storage);}
        }
    }
}
