// ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
// 除非用户明确要求修改本背景功能，否则不要改动本 Shader 及 CombatBackground* / CombatSpaceBackground* 链路。
Shader "TopDog/CombatSkyboxEquirectInterior"
{
    Properties
    {
        _MainTex ("Equirect", 2D) = "" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Front
        ZWrite Off
        ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                float u = atan2(dir.z, dir.x) * (0.5 / 3.14159265) + 0.5;
                float v = asin(clamp(dir.y, -1.0, 1.0)) * (1.0 / 3.14159265) + 0.5;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u, v));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
