Shader "Hidden/Photography/SnapshotUtility"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _SavedDepth;
            float4 _PhotoClip; // near, far, reversed-Z, unused
            float frag(v2f_img i):SV_Target
            {
                float d=tex2D(_SavedDepth,i.uv).r;
                if(_PhotoClip.z>.5)d=1-d;
                return _PhotoClip.x*_PhotoClip.y/max(.000001,_PhotoClip.y-d*(_PhotoClip.y-_PhotoClip.x));
            }
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Black;
            float frag(v2f_img i):SV_Target
            { return saturate(dot(tex2D(_MainTex,i.uv).rgb-tex2D(_Black,i.uv).rgb,float3(1.0/3,1.0/3,1.0/3))); }
            ENDHLSL
        }
    }
}

