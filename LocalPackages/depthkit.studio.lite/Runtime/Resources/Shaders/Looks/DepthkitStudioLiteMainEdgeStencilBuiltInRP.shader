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

Shader "Depthkit/Studio/Depthkit Studio Lite Main Edge Stencil Built-in RP"
{
    Properties
    {
        [Toggle(DK_USE_EDGEMASK)] _UseEdgeMask("Use Edge Mask", Float) = 0
    }

    SubShader
    {
        Tags {"RenderType" = "Opaque" "Queue" = "AlphaTest"}
        Cull Back
        LOD 100

        Pass
        {
            Name "Main Edge Stencil"
            Tags {"LightMode" = "Always"}
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }
            ColorMask 0
            ZWrite Off

            CGPROGRAM

            #pragma shader_feature_local __ DK_USE_EDGEMASK

            #pragma vertex vert
            #pragma fragment frag
            #pragma fragmentoption ARB_precision_hint_fastest

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #define DK_USE_BUILT_IN_COLOR_CONVERSION
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/Depthkit.cginc"
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleCoreTriangles.cginc"
            #define DK_SCREEN_DOOR_TRANSPARENCY
            #include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/SampleEdgeMask.cginc"
            #include "Packages/nyc.scatter.depthkit.studio.lite/Runtime/Resources/Shaders/Looks/DepthkitStudioLiteGeomOnly.cginc"
            ENDCG
        }
    }
}
