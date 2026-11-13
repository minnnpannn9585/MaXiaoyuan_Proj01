Shader "Game/Sky/Celestial"
{
    Properties
    {
        _SunDirection("Sun direction", Vector) = (0,1,0,0)
        _MoonDirection("Moon direction", Vector) = (0,-1,0,0)
        _Daylight("Daylight", Float) = 1
        _MoonRadius("Moon radius", Float) = .007
        _Horizon("Horizon", Color) = (.5,.6,.7,1)
        _NightBrightness("Night brightness", Float) = .08
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float3 _SunDirection, _MoonDirection;
            float _Daylight, _MoonRadius, _NightBrightness;
            float4 _Horizon;
            struct v2f { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
            v2f vert(float4 v:POSITION) { v2f o; o.pos=UnityObjectToClipPos(v); o.dir=v.xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float fbm(float2 p) { return noise(p)*.57+noise(p*2.03)*.28+noise(p*4.11)*.15; }
            float4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.dir);
                float above=smoothstep(0,.012,d.y);
                float3 night=float3(.13,.21,.4)*_NightBrightness;
                float3 zenith=lerp(night,float3(.12,.32,.68),_Daylight);
                float3 c=lerp(_Horizon.rgb,zenith,pow(saturate(d.y),.4));
                float dusk=(1-smoothstep(.02,.3,abs(_SunDirection.y)))*_Daylight;
                c+=float3(.55,.16,.035)*dusk*pow(saturate(dot(d,_SunDirection)),8)*exp(-abs(d.y)*6);
                // Celestial discs are directional and clipped by the horizon, including reflections.
                float sunAngle=length(d-normalize(_SunDirection));
                float sunVisible=smoothstep(-.005,.005,_SunDirection.y)*above;
                c+=float3(1,.8,.5)*(1-smoothstep(.004,.005,sunAngle))*8*sunVisible;
                c+=float3(1,.55,.2)*exp(-sunAngle*28)*.25*sunVisible;
                float moonAngle=length(d-normalize(_MoonDirection));
                float moonDisc=1-smoothstep(_MoonRadius*.92,_MoonRadius,moonAngle);
                float moonVisible=smoothstep(0,.015,_MoonDirection.y)*above;
                float craters=.72+.28*fbm(d.xz*1800);
                c=lerp(c,float3(.8,.87,1)*craters,moonDisc*moonVisible);
                c+=float3(.1,.15,.25)*exp(-moonAngle*65)*moonVisible;
                // Slowly advected cloud layers; no photographed sun baked into the sky.
                float2 p=d.xz/(max(0,d.y)+.18)*2.5+_Time.y*float2(.004,.001);
                float cloud=smoothstep(.52,.76,fbm(p))*smoothstep(.04,.25,d.y);
                float3 cloudColor=lerp(night*1.8,float3(.78,.81,.85),_Daylight);
                c=lerp(c,cloudColor,cloud*.85);
                return float4(max(c,0),1);
            }
            ENDHLSL
        }
    }
}
