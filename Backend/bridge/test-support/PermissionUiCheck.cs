using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using TMPro;
public static class PermissionUiCheck {
 static FieldInfo F(string n)=>typeof(Seyra.UI.OpenCodeTestChat).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic);
 static T Get<T>(object c,string n)=>(T)F(n).GetValue(c);
 static async Task Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(12);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("UI check timed out");await Task.Delay(50);}}
 public static async Task<string> Run(){
 var c=UnityEngine.Object.FindFirstObjectByType<Seyra.UI.OpenCodeTestChat>();
 if(!Application.isPlaying)throw new Exception("Play Mode required");
 c.enabled=false;F("serverUrl").SetValue(c,"http://127.0.0.1:4199");c.enabled=true;
 try{
 await Wait(()=>Get<bool>(c,"connected"));
 for(int round=0;round<2;round++){
 Get<TMP_InputField>(c,"input").text="Test permission";Get<UnityEngine.UI.Button>(c,"sendButton").onClick.Invoke();
 await Wait(()=>Get<GameObject>(c,"permissionPanel").activeSelf);
 if(Get<TMP_Text>(c,"permissionDetails").richText)throw new Exception("Unsafe rich text");
 var button=Get<UnityEngine.UI.Button>(c,round==0?"allowPermissionButton":"rejectPermissionButton");
 if(!button.interactable)throw new Exception("Decision disabled");button.onClick.Invoke();button.onClick.Invoke();
 await Wait(()=>!Get<bool>(c,"sending"));
 if(Get<GameObject>(c,"permissionPanel").activeSelf||!Get<UnityEngine.UI.Button>(c,"sendButton").interactable)throw new Exception("Turn did not resume");
 }
 return "PASS: pending display, literal details, allow once, reject, duplicate-click guard, turn continuation, panel hidden and send restored";
 }finally{c.enabled=false;F("serverUrl").SetValue(c,"http://127.0.0.1:4096");}
 }
}
