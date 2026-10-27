using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        static readonly string[] appearanceNames={"matte_opaque","colored_translucent","unlit","ui_unlit","plastic","transparent"};
        static JObject AppearanceJson(ManagedAppearance appearance)=>AppearanceJson(appearance.Style,appearance.Color);
        static JObject AppearanceJson(AppearanceStyle style,Color color)=>new JObject{["style"]=appearanceNames[(int)style],["color"]=new JObject{["r"]=color.r,["g"]=color.g,["b"]=color.b,["a"]=color.a}};
        static (AppearanceStyle style,Color color) ReadAppearance(JObject args)
        {
            var index=Array.IndexOf(appearanceNames,Text(args,"style"));
            if(index<0)throw new ArgumentException("Unknown appearance style.");
            var rgba=args["color"] as JObject??throw new ArgumentException("color must be an RGBA object.");
            Keys(rgba,new[]{"r","g","b","a"});
            float Channel(string name)
            {
                var token=rgba[name];
                if(token.Type!=JTokenType.Float&&token.Type!=JTokenType.Integer)throw new ArgumentException("RGBA channels must be numbers.");
                var value=(double)token;
                if(double.IsNaN(value)||double.IsInfinity(value)||value<0||value>1)throw new ArgumentException("RGBA channels must be finite numbers in [0,1].");
                return (float)value;
            }
            var color=new Color(Channel("r"),Channel("g"),Channel("b"),Channel("a"));
            var style=(AppearanceStyle)index;
            if(!ManagedAppearance.SupportsAlpha(style)&&(double)rgba["a"]!=1)throw new ArgumentException("This style requires alpha=1.");
            return (style,color);
        }
        JObject SetAppearance(JObject args)
        {
            Keys(args,new[]{"entityId","style","color"});
            var value=ReadAppearance(args);var go=Find(args["entityId"]);
            var record=go.GetComponent<GeneratedObject>();
            if(go.transform==generated || !go.transform.IsChildOf(generated) || record==null || record.Kind!="primitive")throw new ArgumentException("Appearance tools only modify managed primitives inside Generated.");
            var appearance=go.GetComponent<ManagedAppearance>();
            if(appearance==null || !appearance.IsIntact())throw new InvalidOperationException("Appearance was changed externally or is not managed.");
            appearance.SetAppearance(value.style,value.color);
            return Inspect(go);
        }
    }
}
