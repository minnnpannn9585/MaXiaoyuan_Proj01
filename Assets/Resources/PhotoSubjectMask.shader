Shader "Hidden/Photography/SubjectMask"
{
    Properties
    {
        _BaseMap("Subject alpha", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Float) = 0.5
        _AlphaClip("Alpha clip", Float) = 0
        _MaskValue("Mask value", Float) = 1
        _Cull("Cull", Float) = 2
        _ZTest("Depth test", Float) = 4
        _ZWrite("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff, _AlphaClip, _MaskValue;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_AlphaClip > 0.5)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return half4(_MaskValue, _MaskValue, _MaskValue, 1);
            }
            ENDHLSL
        }
    }
}
