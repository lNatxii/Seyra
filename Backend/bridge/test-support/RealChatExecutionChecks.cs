using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Networking;
using Seyra.SceneTools;
using Seyra.UI;
using TMPro;
using UnityEngine;

public static class RealChatExecutionChecks
{
    static OpenCodeTestChat chat;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Get<T>(string name)=>(T)typeof(OpenCodeTestChat).GetField(name,Flags).GetValue(chat);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Wait(Func<bool> condition,string message){var end=DateTime.UtcNow.AddSeconds(90);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException(message+": "+chat.ExecutionState);await Task.Delay(100);}}
    static void Click(string name){var button=Get<UnityEngine.UI.Button>(name);Check(button.interactable,"Button disabled: "+name);button.onClick.Invoke();}
    static int Rows()=>Get<UnityEngine.UI.ScrollRect>("conversation").content.GetComponentsInChildren<TMP_Text>().Length;
    static JObject Query(string tool,JObject arguments)
    {
        var runtime=UnityEngine.Object.FindFirstObjectByType<SeyraSceneRuntime>();
        var response=JObject.Parse(runtime.Tools.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=arguments,["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(1).ToString("O")}.ToString()));
        Check((bool)response["success"],"Runtime inspection failed: "+response);
        return (JObject)response["data"];
    }
    static async Task<JObject> Server(string suffix)=>await Get<JsonHttpClient>("http").RequestAsync("GET","/api/session/"+Get<string>("sessionId")+suffix,null,CancellationToken.None);
    static void Send(string text){Get<TMP_InputField>("input").text=text;Click("sendButton");}
    public static async Task<string> Run()
    {
        Check(Application.isPlaying,"Play Mode required");
        Application.runInBackground=true;
        chat=UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        await Wait(()=>Get<bool>("connected")&&Get<UnityEngine.UI.Button>("sendButton").interactable,"Real connection");
        var session=Get<string>("sessionId");
        var initial=await Server("");
        Send("Prueba de cancelación en orden estricto, sin paralelizar los pasos. Usa directamente las herramientas seyra_. Abre una petición llamada CancelValidation02 y crea un cubo BeforeCancel02 en posición local (0,1,2), rotación (0,0,0) y escala (1,1,1). Solo cuando su creación esté confirmada, usa read para leer únicamente las primeras tres líneas de C:/Proyectos/Seyra/Backend/README.md y espera el permiso. Solo después de completar esa lectura crea un cubo AfterCancel02 con el mismo Transform. Por último cierra la petición. No uses shell ni otras herramientas.");
        await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Real pending permission");
        var permission=(JObject)Get<JObject>("pendingPermission").DeepClone();
        Check((string)permission["action"]=="read","Unexpected permission action");
        var before=Query("scene_find",new JObject{["name"]="BeforeCancel02",["match"]="exact"});
        Check((int)before["total"]==1,"First creation was not confirmed before permission");
        Check((int)Query("scene_find",new JObject{["name"]="AfterCancel02",["match"]="exact"})["total"]==0,"Model did not respect the sequential gate");
        var rowCount=Rows();
        Click("reconnectButton");
        await Wait(()=>Get<bool>("connected")&&!Get<bool>("connecting")&&chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Real reconnect");
        Check(Get<string>("sessionId")==session&&Rows()==rowCount,"Reconnect duplicated the transcript or session");
        chat.enabled=false;chat.enabled=true;
        await Wait(()=>Get<bool>("connected")&&!Get<bool>("connecting")&&chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Real re-enable");
        Check(Get<string>("sessionId")==session&&Rows()==rowCount,"Re-enable duplicated the transcript or session");
        Check((string)Get<JObject>("pendingPermission")["id"]==(string)permission["id"],"Permission identity changed");
        Click("cancelButton");
        Get<UnityEngine.UI.Button>("cancelButton").onClick.Invoke();
        await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Cancelled,"Real cancellation");
        await Task.Delay(1500);
        var stopped=await Server("");
        Check((string)stopped["data"]?["outcome"]=="interrupted","Server did not confirm interrupted outcome");
        Check(((JArray)(await Server("/permission"))["data"]).Count==0,"Cancellation left permissions pending");
        var retained=Query("scene_find",new JObject{["name"]="BeforeCancel02",["match"]="exact"});
        var absent=Query("scene_find",new JObject{["name"]="AfterCancel02",["match"]="exact"});
        Check((int)retained["total"]==1&&(int)absent["total"]==0,"Cancellation reverted prior work or allowed a subsequent creation");
        Check(!Get<UnityEngine.UI.Button>("sendButton").interactable,"Interrupted work was silently dismissed");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp"));
        Click("closeTurnButton");
        Send("El turno anterior fue cancelado. Consulta request_list y cierra explícitamente la petición CancelValidation02 si sigue abierta. Después consulta BeforeCancel02 para confirmar que sigue existiendo y que AfterCancel02 no se creó. No crees ni elimines objetos; usa solo las herramientas seyra_ directas.");
        await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Continuation in original session");
        Check(Get<string>("sessionId")==session,"Continuation changed conversation");
        var requests=Query("request_list",new JObject());
        Check((string)requests["activeRequestGroupId"]==null,"Interrupted content request was not closed explicitly");
        var messages=(JArray)(await Server("/message?order=desc&limit=100"))["data"];
        Check(messages.OfType<JObject>().Count(m=>(string)m["type"]=="user")==2,"Prompt duplicated in real session");
        var tools=new JArray(messages.OfType<JObject>().SelectMany(m=>(m["content"] as JArray??new JArray()).OfType<JObject>()).Where(p=>(string)p["type"]=="tool").Select(p=>new JObject{["name"]=p["name"],["id"]=p["id"],["status"]=p["state"]?["status"],["input"]=p["state"]?["input"],["error"]=p["state"]?["error"]}));
        var evidence=new JObject{["sessionId"]=session,["model"]=initial["data"]?["model"],["permission"]=permission,["serverInterrupted"]=stopped["data"]?["outcome"],["sameSessionAfterReconnectAndReenable"]=true,["unchangedRowCount"]=rowCount,["retainedBeforeCancellation"]=retained,["absentAfterCancellation"]=absent,["contentRequests"]=requests,["userMessages"]=2,["tools"]=tools};
        File.WriteAllText(Path.Combine(directory,"02-real-execution.json"),evidence.ToString());
        return evidence.ToString();
    }
}
