#ifndef SEYRA_SURFACE_INCLUDED
#define SEYRA_SURFACE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(SEYRA_LIT)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif
CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
CBUFFER_END
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
Varyings SurfaceVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}
half4 SurfaceFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#if defined(SEYRA_LIT)
    InputData lighting = (InputData)0;
    lighting.positionWS = input.positionWS;
    lighting.positionCS = input.positionCS;
    lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
    lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    lighting.bakedGI = SampleSH(lighting.normalWS);
    lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    lighting.shadowMask = half4(1,1,1,1);
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = _BaseColor.rgb;
    surface.alpha = _BaseColor.a;
    surface.normalTS = half3(0,0,1);
    surface.occlusion = 1;
#if defined(SEYRA_PLASTIC)
    surface.specular = half3(0.18,0.18,0.18);
    surface.smoothness = 0.65;
#endif
    half4 result = UniversalFragmentBlinnPhong(lighting, surface);
#else
    half4 result = _BaseColor;
#endif
#if !defined(SEYRA_ALPHA)
    result.a = 1;
#endif
    return result;
}
#endif
