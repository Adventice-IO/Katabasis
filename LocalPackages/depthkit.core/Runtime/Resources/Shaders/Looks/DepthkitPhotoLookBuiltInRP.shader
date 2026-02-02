/************************************************************************************

Depthkit Unity SDK License v1
Copyright 2016-2024 Simile Inc dba Scatter. All Rights reserved.  

Licensed under the the Simile Inc dba Scatter ("Scatter")
Software Development Kit License Agreement (the "License"); 
you may not use this SDK except in compliance with the License, 
which is provided at the time of installation or download, 
or which otherwise accompanies this software in either electronic or hard copy form.  

You may obtain a copy of the License at http://www.depthkit.tv/license-agreement-v1

Unless required by applicable law or agreed to in writing, 
the SDK distributed under the License is distributed on an "AS IS" BASIS, 
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
See the License for the specific language governing permissions and limitations under the License. 

************************************************************************************/

// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Depthkit/Core/Depthkit Core Photo Look Built-in RP"
{
    Properties
    {
        _ShadowAmount ("Shadow Amount", Range (0.0,1.0)) = 1.0
        [Toggle(DK_USE_LIGHTPROBES)] _SampleProbes("Use Light Probes", Float) = 1
        [Toggle(DK_USE_EDGEMASK)] _UseEdgeMask("Use Edge Mask", Float) = 0
        [Toggle(DK_DEBUG_EDGEMASK)] _RenderMaskOnGeo("Debug Edge Mask", Float) = 0
        [Toggle(DK_NO_MAIN_LIGHT)] _NoMainLight("Disable Main Directional Shadows", Float) = 0
        [Toggle(DK_USE_DEBUG_COLOR)] _DebugColor("Debug Per Perspective Color", Float) = 0
        [Toggle(DK_SCREEN_DOOR_TRANSPARENCY)] _DitherEdge("Dither Edge", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull off   
        LOD 100

        Pass
        {
            Tags {"LightMode" = "ForwardBase"}
            CGPROGRAM

            #pragma shader_feature_local __ DK_USE_LIGHTPROBES
            #pragma shader_feature_local __ DK_USE_DEBUG_COLOR
            #pragma shader_feature_local __ DK_USE_EDGEMASK
            #pragma shader_feature_local __ DK_DEBUG_EDGEMASK
            #pragma shader_feature_local __ DK_NO_MAIN_LIGHT
            #pragma shader_feature_local __ DK_SCREEN_DOOR_TRANSPARENCY

            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #define DK_USE_BUILT_IN_COLOR_CONVERSION
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/Depthkit.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleCoreTriangles.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleEdgeMask.cginc"
            #define DK_FORWARDBASE_PASS
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Looks/DepthkitCorePhotoLook.cginc"

            ENDCG
        }

        Pass
        {
            Tags {"LightMode" = "ForwardAdd"}
            BlendOp Max
            Blend One One
            ZWrite Off

            CGPROGRAM

            #pragma shader_feature_local __ DK_USE_LIGHTPROBES
            #pragma shader_feature_local __ DK_USE_EDGEMASK
            #pragma shader_feature_local __ DK_USE_DEBUG_COLOR
            #pragma shader_feature_local __ DK_SCREEN_DOOR_TRANSPARENCY

            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile_fwdadd_fullshadows

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #define DK_USE_BUILT_IN_COLOR_CONVERSION
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/Depthkit.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleCoreTriangles.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleEdgeMask.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Looks/DepthkitCorePhotoLook.cginc"
            ENDCG
        }
        
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
        
            Fog {Mode Off}
            ZWrite On ZTest LEqual Cull Off
            Offset 1, 1
    
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local __ DK_USE_EDGEMASK
            #pragma shader_feature_local __ DK_SCREEN_DOOR_TRANSPARENCY

            #pragma multi_compile_shadowcaster
            #pragma fragmentoption ARB_precision_hint_fastest
            #include "UnityCG.cginc"
            #define DK_USE_BUILT_IN_COLOR_CONVERSION
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/Depthkit.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleCoreTriangles.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleEdgeMask.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Looks/DepthkitCoreShadowCaster.cginc"
            ENDCG
        }
    }
    Fallback "VertexLit"
}
