using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace Seyra.SceneTools
{
    public enum AppearanceStyle { MatteOpaque, ColoredTranslucent, Unlit, UiUnlit, Plastic, Transparent }

    // Owns exactly one runtime material. History stores values, never copies of materials.
    [DisallowMultipleComponent]
    public sealed class ManagedAppearance : MonoBehaviour
    {
        [SerializeField] AppearanceStyle style;
        [SerializeField] Color color = Color.white;
        [SerializeField, HideInInspector] Material ownedMaterial;
        public AppearanceStyle Style => style;
        public Color Color => color;
        internal Material OwnedMaterial => ownedMaterial;
        static readonly Shader[] shaders=new Shader[6];
        internal static bool SupportsAlpha(AppearanceStyle value) => value == AppearanceStyle.ColoredTranslucent || value == AppearanceStyle.UiUnlit || value == AppearanceStyle.Transparent;
        internal static Shader LoadShader(AppearanceStyle value)
        {
            if(!Enum.IsDefined(typeof(AppearanceStyle),value))throw new ArgumentException("Unknown appearance style.");
            var shader=shaders[(int)value];
            if(shader==null)shaders[(int)value]=shader=Resources.Load<Shader>("Seyra/Appearance/"+value);
            if(shader==null || !shader.isSupported)throw new InvalidOperationException("Appearance shader is missing or unsupported: "+value);
            return shader;
        }
        public void SetAppearance(AppearanceStyle value,Color rgba)
        {
            if(!Enum.IsDefined(typeof(AppearanceStyle),value))throw new ArgumentException("Unknown appearance style.");
            foreach(var channel in new[]{rgba.r,rgba.g,rgba.b,rgba.a})
                if(float.IsNaN(channel)||float.IsInfinity(channel)||channel<0||channel>1)throw new ArgumentException("Color channels must be finite numbers in [0,1].");
            if(!SupportsAlpha(value)&&rgba.a!=1)throw new ArgumentException("This style requires alpha=1.");
            var renderer=GetComponent<MeshRenderer>();var graphic=GetComponent<Graphic>();
            if(value==AppearanceStyle.UiUnlit ? graphic==null&&renderer==null : renderer==null)
                throw new ArgumentException("This style requires a MeshRenderer; UiUnlit also supports a uGUI Graphic.");
            var shader=LoadShader(value);
            if(ownedMaterial==null)ownedMaterial=new Material(shader){name="Seyra appearance (owned)",hideFlags=HideFlags.DontSave};
            else ownedMaterial.shader=shader;
            ownedMaterial.shaderKeywords=Array.Empty<string>();
            ownedMaterial.renderQueue=-1;
            ownedMaterial.SetColor("_BaseColor",rgba);
            if(value==AppearanceStyle.UiUnlit)
            {
                ownedMaterial.SetTexture("_MainTex",null);
                ownedMaterial.SetTextureScale("_MainTex",Vector2.one);
                ownedMaterial.SetTextureOffset("_MainTex",Vector2.zero);
                ownedMaterial.SetFloat("_StencilComp",8);ownedMaterial.SetFloat("_Stencil",0);ownedMaterial.SetFloat("_StencilOp",0);
                ownedMaterial.SetFloat("_StencilWriteMask",255);ownedMaterial.SetFloat("_StencilReadMask",255);
                ownedMaterial.SetFloat("_ColorMask",15);ownedMaterial.SetFloat("_UseUIAlphaClip",0);
            }
            style=value;color=rgba;
            if(renderer!=null)
            {
                renderer.sharedMaterial=ownedMaterial;
                renderer.shadowCastingMode=SupportsAlpha(value)?ShadowCastingMode.Off:ShadowCastingMode.On;
            }
            if(graphic!=null){graphic.material=ownedMaterial;graphic.SetMaterialDirty();}
        }
        internal bool IsIntact()
        {
            var renderer=GetComponent<MeshRenderer>();var graphic=GetComponent<Graphic>();
            if(ownedMaterial==null || ownedMaterial.shader!=LoadShader(style) || ownedMaterial.GetColor("_BaseColor")!=color || ownedMaterial.renderQueue!=ownedMaterial.shader.renderQueue || ownedMaterial.shaderKeywords.Length!=0)return false;
            if(renderer!=null && (renderer.sharedMaterials.Length!=1 || renderer.sharedMaterial!=ownedMaterial || renderer.HasPropertyBlock() || renderer.shadowCastingMode!=(SupportsAlpha(style)?ShadowCastingMode.Off:ShadowCastingMode.On)))return false;
            if(style==AppearanceStyle.UiUnlit && (ownedMaterial.GetTexture("_MainTex")!=null || ownedMaterial.GetTextureScale("_MainTex")!=Vector2.one || ownedMaterial.GetTextureOffset("_MainTex")!=Vector2.zero ||
                ownedMaterial.GetFloat("_StencilComp")!=8 || ownedMaterial.GetFloat("_Stencil")!=0 || ownedMaterial.GetFloat("_StencilOp")!=0 ||
                ownedMaterial.GetFloat("_StencilWriteMask")!=255 || ownedMaterial.GetFloat("_StencilReadMask")!=255 || ownedMaterial.GetFloat("_ColorMask")!=15 || ownedMaterial.GetFloat("_UseUIAlphaClip")!=0))return false;
            foreach(var pass in new[]{"Forward","DepthOnly","ShadowCaster","UiUnlit"})if(!ownedMaterial.GetShaderPassEnabled(pass))return false;
            return graphic==null || graphic.material==ownedMaterial;
        }
        internal string Signature => style+"|"+ColorUtility.ToHtmlStringRGBA(color)+"|"+(IsIntact()?"intact":"modified");
        void OnDestroy()
        {
            if(ownedMaterial==null)return;
#if UNITY_EDITOR
            if(!Application.isPlaying)DestroyImmediate(ownedMaterial);else Destroy(ownedMaterial);
#else
            Destroy(ownedMaterial);
#endif
            ownedMaterial=null;
        }
    }
}

