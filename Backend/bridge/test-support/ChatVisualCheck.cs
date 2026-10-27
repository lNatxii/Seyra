using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Seyra.UI;
using TMPro;
using UnityEngine;

public static class ChatVisualCheck
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static OpenCodeTestChat Chat=>UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
    static T Get<T>(string name)=>(T)typeof(OpenCodeTestChat).GetField(name,Flags).GetValue(Chat);
    static void Set(string name,object value)=>typeof(OpenCodeTestChat).GetField(name,Flags).SetValue(Chat,value);
    static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(12);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Visual fixture timed out");await Task.Delay(50);}}
    static void Click(string name)=>Get<UnityEngine.UI.Button>(name).onClick.Invoke();
    public static async Task<string> Interrupted()
    {
        Chat.enabled=false;
        Set("sessionId",null);Set("activeUserId",null);Set("pendingText",null);Set("serverUrl","http://127.0.0.1:4202");
        var template=Get<TMP_Text>("messageTemplate");
        var content=Get<UnityEngine.UI.ScrollRect>("conversation").content;
        foreach(var row in content.Cast<Transform>().Where(t=>t!=template.transform).ToArray())UnityEngine.Object.DestroyImmediate(row.gameObject);
        Get<IDictionary>("messageRows").Clear();
        Chat.enabled=true;
        await Wait(()=>Get<bool>("connected")&&!Get<bool>("connecting"));
        Get<TMP_InputField>("input").text="permission";Click("sendButton");
        await Wait(()=>Chat.ExecutionState==OpenCodeTestChat.TurnState.WaitingForPermission);
        Click("cancelButton");
        await Wait(()=>Chat.ExecutionState==OpenCodeTestChat.TurnState.Cancelled);
        return Get<TMP_Text>("executionLabel").text;
    }
    public static async Task<string> Completed()
    {
        Click("closeTurnButton");
        Get<TMP_InputField>("input").text="complete";Click("sendButton");
        await Wait(()=>Chat.ExecutionState==OpenCodeTestChat.TurnState.Completed);
        return Get<TMP_Text>("executionLabel").text;
    }
}
