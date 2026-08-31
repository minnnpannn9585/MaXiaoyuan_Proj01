    Shader"Hidden/Gaia/FilterCurvatureMask" {

    Properties {
				//The main texture (existing heightmap)
				_NormalMapTex("Normalmap Texture", any) = "" {}
				//The input texture
				_InputTex ("Input Texture", any) = "" {}				
				_HeightTex ("Height Texture", any) = "" {}				
				//The blurred heightmap texture
				_BlurredHeightTex ("Blurred Height Texture", any) = "" {}	

				_BlurAmount("Blur Amount", Range(0.001, 0.1)) = 0.001
				_WorldUnits("World Units", Float) = -400
				_Intensity("Intensity", Float) = 0.7
				_BlurSteps("Blur Steps", Int) = 32
				_BlurRadialSteps("Blur Radial Steps", Int) = 16
				
				 }

    SubShader {

        ZTest Always Cull Off ZWrite Off

        CGINCLUDE

            #include "UnityCG.cginc"
            #include "TerrainTool.cginc"
			#include "../../../Shaders/Terrain.cginc"


            sampler2D _InputTex;
			sampler2D _HeightTex;
			sampler2D _BlurredHeightTex;
			sampler2D _HeightTransformTex;
			
			float _BlurAmount;
			float _WorldUnits;
			float _Intensity;
			int _BlurSteps;
			int _BlurRadialSteps;

            struct appdata_t {
                float4 vertex : POSITION;
                float2 pcUV : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 pcUV : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.pcUV = v.pcUV;
                return o;
            }

			/*
			MIT License

			Copyright (c) 2018 @XorDev

			Permission is hereby granted, free of charge, to any person obtaining a copy
			of this software and associated documentation files (the "Software"), to deal
			in the Software without restriction, including without limitation the rights
			to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
			copies of the Software, and to permit persons to whom the Software is
			furnished to do so, subject to the following conditions:

			The above copyright notice and this permission notice shall be included in all
			copies or substantial portions of the Software.

			THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
			IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
			FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
			AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
			LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
			OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
			SOFTWARE.
			*/

			//Blur function adapted from: https://github.com/GameMakerDiscord/blur-shaders/tree/master
			float blur(sampler2D tex, float2 uv, float amount)
			{
				int Quality = _BlurSteps;
				int Directions = _BlurRadialSteps;
				float Pi = 6.28318530718; //pi * 2
	
	
				float2 radius = float2(amount, amount);
				float Color = tex2D(tex, uv).r;
				for (float d = 0.0; d < Pi; d += Pi / float(Directions))
				{
					for (float i = 1.0 / float(Quality); i <= 1.0; i += 1.0 / float(Quality))
					{
						Color += tex2D(tex, uv +float2(cos(d), sin(d)) * radius * i).r;
					}
				}
				Color /= float(Quality) * float(Directions) + 1.0;
				return Color;
			}

			float GetFilter(v2f i)
			{	
				float origin = InternalUnpackHeightmap(tex2D(_HeightTex, i.pcUV));
				float blurred = blur(_HeightTex, i.pcUV, _BlurAmount);
				float curv = saturate(abs(pow((origin - blurred) * _WorldUnits, _Intensity)));
				return curv;
	
	
			}


		ENDCG
            

         Pass    // 0 Slope Mask Multiply
        {
            Name "Slope Mask Multiply"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment SlopeMaskMultiply

            float4 SlopeMaskMultiply(v2f i) : SV_Target
            {
				float height = (tex2D(_InputTex, i.pcUV));
				float filter = GetFilter(i);
				float transformedHeight = (tex2D(_HeightTransformTex, filter));
				float result = height * transformedHeight;
				return result;
			}
            ENDCG
        }

		Pass    // 1 Slope Mask Greater Than
		{
			Name "Slope Mask Greater Than"

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment SlopeMaskGreaterThan

			float4 SlopeMaskGreaterThan(v2f i) : SV_Target
			{
				float height = InternalUnpackHeightmap(tex2D(_InputTex, i.pcUV));
				float filter = GetFilter(i);
				float transformedHeight = InternalUnpackHeightmap(tex2D(_HeightTransformTex, filter));
				float result = height;
				if (transformedHeight > height)
				{
					result = transformedHeight;
				}
				return InternalPackHeightmap(result);
			}
			ENDCG
		}

		Pass    // 2 Slope Mask Smaller Than
		{
			Name "Slope Mask Smaller Than"

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment SlopeMaskSmallerThan

			float4 SlopeMaskSmallerThan(v2f i) : SV_Target
			{
				float height = InternalUnpackHeightmap(tex2D(_InputTex, i.pcUV));
				float filter = GetFilter(i);
				float transformedHeight = InternalUnpackHeightmap(tex2D(_HeightTransformTex, filter));
				float result = height;
				if (transformedHeight < (1 - height))
				{
					result = transformedHeight;
				}
				return InternalPackHeightmap(result);
			}
			ENDCG
		}
	

		Pass    // 3 Slope Mask Add
		{
			Name "Slope Mask Add"

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment SlopeMaskAdd

			float4 SlopeMaskAdd(v2f i) : SV_Target
			{
				float height = InternalUnpackHeightmap(tex2D(_InputTex, i.pcUV));
				float filter = GetFilter(i);
				float transformedHeight = InternalUnpackHeightmap(tex2D(_HeightTransformTex, filter));
				float result = height + transformedHeight;
				return InternalPackHeightmap(result);
			}
			ENDCG
		}

		Pass    // 4 Slope Mask Subtract
		{
			Name "Slope Mask Subtract"

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment SlopeMaskSubtract

			float4 SlopeMaskSubtract(v2f i) : SV_Target
			{
				float height = InternalUnpackHeightmap(tex2D(_InputTex, i.pcUV));
				float filter = GetFilter(i);
				float transformedHeight = InternalUnpackHeightmap(tex2D(_HeightTransformTex, filter));
				float result = height - transformedHeight;
				return InternalPackHeightmap(result);
			}

			ENDCG
		}
	

    }
    Fallback Off
}
