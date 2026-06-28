// ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
// 除非用户明确要求修改本背景功能，否则不要改动本 Shader 及 CombatBackground* / CombatSpaceBackground* 链路。
Shader "TopDog/CombatSkyboxInterior"
{
    Properties
    {
        _Tex ("Cubemap", Cube) = "" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-1000"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Front
        ZWrite Off
        ZTest Always
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.directionWS = positionWS - _WorldSpaceCameraPos;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.directionWS);
                return SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, dir);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
