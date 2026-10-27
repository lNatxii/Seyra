using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Networking;
using Seyra.UI;
using TMPro;
using UnityEngine;

public static class ChatExecutionChecks
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static OpenCodeTestChat chat;
    static JsonHttpClient fixture;
    static T Get<T>(string name) => (T)typeof(OpenCodeTestChat).GetField(name,Flags).GetValue(chat);
    static void Set(string name,object value) => typeof(OpenCodeTestChat).GetField(name,Flags).SetValue(chat,value);
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    static async Task Wait(Func<bool> condition,string name)
    {
        var end=DateTime.UtcNow.AddSeconds(18);
        while(!condition()) {if(DateTime.UtcNow>end)throw new TimeoutException(name+": "+chat.ExecutionState);await Task.Delay(40);}
    }
    static void Click(string name) => Get<UnityEngine.UI.Button>(name).onClick.Invoke();
    static async Task<JObject> State() => await fixture.RequestAsync("GET","/test/state",null,CancellationToken.None);
    static async Task Control(JObject data) => await fixture.RequestAsync("POST","/test/control",data,CancellationToken.None);
    static async Task Start(string mode)
    {
        await Wait(()=>Get<bool>("connected")&&Get<UnityEngine.UI.Button>("sendButton").interactable,"Ready to send");
        Get<TMP_InputField>("input").text=mode;
        Click("sendButton");Click("sendButton");
        Check(chat.ExecutionState==OpenCodeTestChat.TurnState.Sending,"Missing sending state");
    }
    static async Task Close()
    {
        Check(Get<UnityEngine.UI.Button>("closeTurnButton").interactable,"Close is disabled");
        Click("closeTurnButton");Click("closeTurnButton");
        await Wait(()=>Get<UnityEngine.UI.Button>("sendButton").interactable,"Explicit close did not release send");
    }
    static async Task Cancel()
    {
        await Wait(()=>Get<UnityEngine.UI.Button>("cancelButton").interactable,"Cancel not available");
        Click("cancelButton");Click("cancelButton");
        await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Cancelled,"Cancel not confirmed");
        Check(!Get<UnityEngine.UI.Button>("sendButton").interactable,"Interrupted turn was silently discarded");
        Check(Get<TMP_Text>("executionLabel").text.Contains("permanecen"),"Cancellation claims rollback or omits remaining actions");
    }
    public static async Task<string> Run()
    {
        Check(Application.isPlaying,"Play Mode required");
        chat=UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        var originalUrl=Get<string>("serverUrl");
        chat.enabled=false;
        Set("sessionId",null);Set("serverUrl","http://127.0.0.1:4202");
        fixture=new JsonHttpClient("http://127.0.0.1:4202");
        chat.enabled=true;
        var checks=new JArray();
        try
        {
            await Wait(()=>Get<bool>("connected"),"Fixture connection");
            var session=Get<string>("sessionId");
            await Start("permission");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Permission state");
            Check(Get<GameObject>("permissionPanel").activeSelf,"Missing permission panel");
            Click("allowPermissionButton");Click("allowPermissionButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Permission continuation");
            checks.Add("sending, permission, completion and one permission decision");

            await Start("cancel_permission");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Cancel permission state");
            await Cancel();await Close();
            checks.Add("server cancellation while waiting for permission, explicit close, no rollback claim");

            await Start("cancel_during_admission");
            await Task.Delay(200);
            await Cancel();await Close();
            checks.Add("cancellation waits for in-flight admission, duplicate-click guard");

            await Start("disconnect");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"Running state");
            await Control(new JObject{["blockReads"]=true});
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Unresolved&&!Get<bool>("connected"),"Outage state");
            Check(!Get<UnityEngine.UI.Button>("sendButton").interactable,"Outage allowed a duplicate send");
            await Control(new JObject{["blockReads"]=false,["finish"]=true,["session"]=session});
            Click("reconnectButton");Click("reconnectButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed&&Get<bool>("connected"),"Reconnect completion");
            Check(Get<string>("sessionId")==session,"Reconnect created a new session");
            checks.Add("read outage resumes original session and turn without resubmission");

            await Start("drop_prompt");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Lost admission recovery");
            checks.Add("lost POST response reconciled to exactly one server user message");

            await Start("drop_interrupt");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"Lost interrupt setup");
            await Cancel();await Close();
            checks.Add("lost interrupt response inspected without repeating interrupt");

            await Start("disconnect");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"Disable setup");
            chat.enabled=false;chat.enabled=true;
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running&&Get<bool>("connected"),"Re-enable continuity");
            Check(Get<string>("sessionId")==session,"Re-enable changed session");
            await Control(new JObject{["finish"]=true,["session"]=session});
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Re-enable completion");
            checks.Add("disable/re-enable retains active identity and transcript");

            await Start("unknown_tool");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Failed,"Unknown tool outcome accepted as success");
            Check(Get<TMP_Text>("executionLabel").text.Contains("Inspecciona"),"Missing inspection requirement");
            await Close();
            await Start("disconnect");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"History isolation setup");
            chat.enabled=false;chat.enabled=true;
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running&&Get<bool>("connected"),"History isolation reconnect");
            await Control(new JObject{["finish"]=true,["session"]=session});
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Closed unknown result poisoned another turn");
            await Start("fail");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Failed,"Provider failure state");
            await Close();
            checks.Add("unknown runtime result and provider failure are visible and require explicit closure");

            await Start("cancel_race_complete");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"Completion race setup");
            Click("cancelButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Completed,"Finished turn mislabeled cancelled");
            checks.Add("completion/cancel race preserves actual succeeded outcome");

            await Start("lost_permission");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission,"Lost permission setup");
            Click("allowPermissionButton");Click("allowPermissionButton");
            await Task.Delay(200);
            chat.enabled=false;chat.enabled=true;
            await Wait(()=>Get<bool>("connected")&&Get<JObject>("pendingPermission")!=null,"Lost permission reconnect");
            Check(!Get<UnityEngine.UI.Button>("allowPermissionButton").interactable,"Ambiguous permission decision became sendable again");
            await Cancel();await Close();
            checks.Add("lost permission decision remains single-use across re-enable and can be stopped");

            await Start("interrupt_not_received");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Running,"Unacknowledged interrupt setup");
            Click("cancelButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Unresolved&&Get<bool>("connected"),"Unacknowledged interrupt state");
            var beforeRetry=(int)((JArray)(await State())["sessions"]).OfType<JObject>().Single(s=>(string)s["id"]==session)["interrupts"];
            await Task.Delay(1600);
            var afterWait=(int)((JArray)(await State())["sessions"]).OfType<JObject>().Single(s=>(string)s["id"]==session)["interrupts"];
            Check(beforeRetry==afterWait,"Unconfirmed interrupt was automatically repeated");
            await Cancel();await Close();
            checks.Add("unreceived interruption permits an explicit second stop but never an automatic retry");

            await Start("no_admission");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Unresolved&&Get<bool>("connected"),"Unconfirmed submission setup");
            Click("cancelButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Cancelled,"Unconfirmed submission closure");
            Check(Get<TMP_Text>("executionLabel").text.Contains("sigue sin conocerse"),"Unconfirmed submission was presented as a known result");
            await Close();
            checks.Add("unadmitted ambiguous POST is never resubmitted or misrepresented as executed");

            await Start("slow_no_admission");
            await Task.Delay(200);
            Click("cancelButton");
            await Wait(()=>chat.ExecutionState==OpenCodeTestChat.TurnState.Cancelled,"Queued cancellation lost after unconfirmed admission");
            await Close();
            checks.Add("cancellation queued during an unadmitted POST is completed after reconnect");

            await Control(new JObject{["missing"]=true});
            await Wait(()=>Get<bool>("sessionMissing"),"Missing session detection");
            Check(Get<UnityEngine.UI.Button>("closeTurnButton").gameObject.activeSelf,"Missing session cannot be closed explicitly");
            await Control(new JObject{["missing"]=false});
            Click("closeTurnButton");
            await Wait(()=>Get<bool>("connected")&&Get<string>("sessionId")!=session,"Explicit replacement of unavailable session");
            checks.Add("unavailable session requires explicit closure before a fresh conversation");

            var rows=Get<UnityEngine.UI.ScrollRect>("conversation").content.GetComponentsInChildren<TMP_Text>();
            Check(!rows.Any(t=>t.text.Contains("INTERNAL_REASONING_MUST_NOT_RENDER")||t.text.Contains("RAW_INPUT_MUST_NOT_RENDER")),"Internal payload leaked into UI");
            var state=await State();
            var prompts=((JArray)state["events"]).OfType<JObject>().Where(e=>(string)e["type"]=="prompt").ToArray();
            Check(prompts.Length==13,"A submission was lost or repeated: "+prompts.Length);
            Check(rows.Count(t=>t.text=="Tú\ndrop_prompt")==1,"Lost admission duplicated the local echo");
            var sessionState=((JArray)state["sessions"]).OfType<JObject>().Single(s=>(string)s["id"]==session);
            Check((int)sessionState["interrupts"]==9,"Interrupt was automatically retried or duplicated");
            Check((int)sessionState["mutations"]==7,"Cancelled turns continued executing actions");
            Check(((JArray)state["events"]).OfType<JObject>().Count(e=>(string)e["type"]=="decision")==2,"Permission decision duplicated");
            Check(((JArray)state["events"]).OfType<JObject>().Count(e=>(string)e["type"]=="unconfirmed-prompt")==2,"Unconfirmed admission repeated");
            Check(((JArray)state["sessions"]).Count==2,"Reconnect created extra conversations");
            checks.Add("exact prompt/interrupt/decision counts, no duplicate rows, no reasoning or raw tool arguments");
            var output=new JObject{["checks"]=checks,["server"]=state};
            var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.utmp/chat-execution-checks.json"));
            System.IO.File.WriteAllText(path,output.ToString());
            return output.ToString();
        }
        finally
        {
            chat.enabled=false;
            Set("serverUrl",originalUrl);Set("sessionId",null);Set("activeUserId",null);Set("pendingText",null);
            fixture.Dispose();
        }
    }
}
