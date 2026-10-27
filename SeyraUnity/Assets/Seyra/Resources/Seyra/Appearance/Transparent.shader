Shader "Seyra/Transparent"
{
    Properties { [MainColor] _BaseColor("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment
            #pragma multi_compile_instancing
            #define SEYRA_ALPHA 1
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "SeyraSurface.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
