Shader "PW/GTS_URP_Compiled"
{
    Properties
    {
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        // Layer count is passed down to guide height-blend enable/disable, due
        // to the fact that heigh-based blend will be broken with multipass.
        [HideInInspector] [PerRendererData] _NumLayersCount ("Total Layer Count", Float) = 1.0

        // set by terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}

        // used in fallback on old cards & base map
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)

        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}

        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Instanced per-pixel normal", Float) = 1.0


        //GTS Variables
        [Toggle(_HEIGHT_BLEND_ON)]
        _HEIGHT_BLEND("Height Blend", Int) = 0

        [Toggle(_TESSELLATION_ON)]
        _TESSELLATION("Tessellation", Int) = 0

        [Toggle(_SNOW_ON)]
        _SNOW("Snow", Int) = 0

        [Toggle(_RAIN_ON)]
        _RAIN("Rain", Int) = 0

        [Toggle(_DETAIL_NORMALS_ON)]
        _DETAIL_NORMALS("Detail Normals", Int) = 0

        [Toggle(_GEOLOGICAL_ON)]
        _GEOLOGICAL("Geological", Int) = 0

        [Toggle(_MACRO_VARIATION_ON)]
        _MACRO_VARIATION_ON("Macro Variation", Int) = 0

        [Toggle(_MOBILE_VR_ON)]
        _MOBILE_VR_ON("Mobile VR", Int) = 0


        [Toggle]_COLORMAP("_COLORMAP_ON", Float) = 0

        _TerrainPosSize("_TerrainPosSize", Vector) = (0,0,0,0)
        [NoScaleOffset]_Control0("_Control0", 2D) = "black" {}
        [NoScaleOffset]_Control1("_Control1", 2D) = "black" {}
        [NoScaleOffset]_WorldNormalMap("_WorldNormalMap", 2D) = "black" {}
        [NoScaleOffset]_AlbedoArray("_AlbedoArray", 2DArray) = "" {}
        [NoScaleOffset]_NormalArray("_NormalArray", 2DArray) = "" {}
        _BlendFactor("_BlendFactor", Float) = 0.1

        [NoScaleOffset]_SplatmapIndex("_SplatmapIndex", 2D) = "black" {}
        [NoScaleOffset]_SplatmapIndexLowRes("_SplatmapIndexLowRes", 2D) = "black" {}
        _Resolution("_Resolution", Float) = 1024

        [NoScaleOffset]_DetailNormal("_DetailNormal", 2D) = "white" {}
        _DetailNearFarData("_DetailNearFarData", Vector) = (0,0,0,0)

        _GeoNearData("_GeoNearData", Vector) = (1,1,1,1)
        _GeoFarData("_GeoFarData", Vector) = (1,1,1,1)
        [NoScaleOffset]_GeoMap("_GeoMap", 2D) = "black" {}
        [NoScaleOffset]_GeoNormal("_GeoNormal", 2D) = "black" {}
        _GeoLayerData0("_GeoLayerData0", Vector) = (1,1,1,1)
        _GeoLayerData1("_GeoLayerData1", Vector) = (1,1,1,1)

        [NoScaleOffset]_MacroVariationMap("_MacroVariationMap", 2D) = "black" {}
        _MacroVariationData("_MacroVariationData", Vector) = (1, 1, 1, 1)

        _TessellationMultiplier("_TessellationMultiplier", Float) = 1

        _TerrainHeightmapRecipSize("_TerrainHeightmapRecipSize", Vector) = (0,0,0,0)

        _GlobalBlendData("_GlobalBlendData", Vector) = (0,0,0,0)

        [ToggleUI]_WorldAlignedUVs("_WorldAlignedUVs", Float) = 0
        _ObjectSpaceDataA("_ObjectSpaceDataA", Vector) = (0, 0, 0, 0)

        [NoScaleOffset]_ColormapTex("_ColormapTex", 2D) = "black" {}
        [NoScaleOffset]_ColormapNormalTex("_ColormapNormalTex", 2D) = "white" {}
        _ColormapNearFarData("_ColormapNearFarData", Vector) = (1, 1, 1, 1)
        _ColormapDataA("_ColormapDataA", Vector) = (1, 1, 1, 1)


    }

    HLSLINCLUDE

    #pragma multi_compile_fragment __ _ALPHATEST_ON

    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" "TerrainCompatible" = "True"}

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0



            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTERED_RENDERING
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local_fragment _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _MASKMAP
            // Sample normal in pixel shader when doing instancing
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            // -------------------------------------
            // Universal Pipeline keywords

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags{"LightMode" = "UniversalGBuffer"}

            HLSLPROGRAM
            #pragma exclude_renderers gles
            #pragma target 3.0
            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

            //#pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _MASKMAP
            // Sample normal in pixel shader when doing instancing
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #define TERRAIN_GBUFFER 1

            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            
            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment DepthNormalOnlyFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #define SCENESELECTIONPASS
            
            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        // This pass it not used during regular rendering, only for lightmap baking.
        Pass
        {
            Name "Meta"
            Tags{"LightMode" = "Meta"}

            Cull Off

            HLSLPROGRAM
            #pragma vertex TerrainVertexMeta
            #pragma fragment TerrainFragmentMeta

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature EDITOR_VISUALIZATION
            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            //GTS Data
            #pragma shader_feature _GEOLOGICAL_ON
            #pragma shader_feature _HEIGHT_BLEND_ON
            #pragma shader_feature _TESSELLATION_ON
            #pragma shader_feature _DETAIL_NORMALS_ON
            #pragma shader_feature _SNOW_ON
            #pragma shader_feature _RAIN_ON
            #pragma shader_feature _MACRO_VARIATION_ON
            #pragma shader_feature _MOBILE_VR_ON
            #pragma shader_feature _COLORMAP_ON
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GTS_TerrainInput.hlsl"
            #include "../GTS_Functions.hlsl"
            #include "../URP/TerrainLitInput.hlsl"
            #include "../URP/TerrainLitMetaPass.hlsl"

            ENDHLSL
        }

        //UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
