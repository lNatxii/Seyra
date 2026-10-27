using System;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;

public static class PanelPreview
{
    public static string Create()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the panel preview in Play Mode.");
        var demo = UnityEngine.Object.FindFirstObjectByType<UILibraryPreview>();
        if (demo != null) demo.gameObject.SetActive(false);
        var root = new GameObject("__ManagedPanelPreview");
        var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service = new SceneToolService(root.transform, panelCatalog: catalog);
        JObject Vector(float x, float y, float z) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        string Panel(string name, string kind, string title, string text, float x, float y, double width, double height) {
            var result=JObject.Parse(service.Dispatch(new JObject { ["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),
                ["tool"]="create_panel",["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"),["arguments"]=new JObject {
                    ["name"]=name,["parentId"]="Generated",["position"]=Vector(x,y,1.5f),["rotation"]=Vector(0,0,0),["scale"]=Vector(1,1,1),
                    ["panel"]=new JObject{["version"]=1,["kind"]=kind,["title"]=title,["text"]=text,["width"]=width,["height"]=height,["fontSize"]=28} }}.ToString()));
            if(!(bool)result["success"])throw new Exception(result.ToString());return (string)result["data"]["entityId"];
        }
        var text="Este panel se crea mediante un contrato completo.\n\n"+
            "El contenido, las dimensiones y el tamaño de letra se editan juntos. La pose conserva su identidad.\n\n"+
            "• Guardado y carga con el contenido generado.\n• Papelera y restauración.\n• Deshacer por operación o petición.\n\n"+
            "<b>Este texto se muestra literalmente.</b>\n\n"+
            string.Join("\n\n",System.Linq.Enumerable.Repeat("El cuerpo largo se desplaza dentro del panel, sin invadir el título ni otras ventanas.",8));
        var panel=Panel("PreviewPanel","panel","SeYRA · Panel gestionado",text,-.5f,1.4f,.8,.7);
        var label=Panel("PreviewLabel","label","","Etiqueta espacial\nTexto independiente y persistible",.5f,1.6f,.8,.16);
        Panel("PreviewEllipsis","label","","Una etiqueta con mucho texto conserva sus dimensiones: el contenido que no cabe termina con puntos suspensivos. "+text,.5f,1.3f,.8,.12);
        return new JObject{["panelId"]=panel,["labelId"]=label,["created"]=true}.ToString();
    }
}
