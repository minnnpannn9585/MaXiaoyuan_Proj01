Shader "Hidden/Photography/ExposureAccumulate"
{
    Properties { _MainTex("Sample", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Weight;
            float4 frag(v2f_img i) : SV_Target
            {
                float3 color = tex2D(_MainTex, i.uv).rgb;
                // Match the previous average of encoded Color32 photo samples.
                #ifndef UNITY_COLORSPACE_GAMMA
                    color = LinearToGammaSpace(color);
                #endif
                return float4(color, 1) * _Weight;
            }
            ENDHLSL
        }
    }
}
