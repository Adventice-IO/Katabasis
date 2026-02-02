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

#ifndef _DEPTHKIT_STUDIO_UNIFORMS_CGINC
#define _DEPTHKIT_STUDIO_UNIFORMS_CGINC

//TODO per perspective edge blending is unnecessary if the feature is disabled
struct PerspectiveColorBlending
{
    int enablePerspective;
    float viewWeightPowerContribution;
};

float _PerViewDisparityThreshold = 0.05f;
float _PerViewDisparityBlendWidth = 0.05f;
float _SurfaceNormalColorBlendingPower = 1.0f;
float _GlobalViewDependentColorBlendWeight = 1.0f;
float3 _UntexturedFragDefaultColor;
float _InvalidateEdgeWidth = 0.5f;


StructuredBuffer<PerspectiveColorBlending> _PerspectiveColorBlending;

#endif //_DEPTHKIT_CORE_UNIFORMS_CGINC