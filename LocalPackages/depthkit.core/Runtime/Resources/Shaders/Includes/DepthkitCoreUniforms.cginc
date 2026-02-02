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

#ifndef _DEPTHKIT_CORE_UNIFORMS_CGINC
#define _DEPTHKIT_CORE_UNIFORMS_CGINC

// CLIP DATA
float _EdgeChoke = 0.5; // per-pixel brightness threshold, used to refine edge geometry from eroneous edge depth samples
StructuredBuffer<PerspectiveData> _PerspectiveDataStructuredBuffer;
int _PerspectivesCount;
int _PerspectivesInX;
int _PerspectivesInY;
int _TextureFlipped;
int _ColorSpaceCorrectionDepth;
int _ColorSpaceCorrectionColor;

Texture2D<float4> _CPPTexture;
SamplerState _LinearClamp;

// MESH SOURCE DATA
// The datatype for the per perspective bias is a float4 because float arrays get pushed to the shader as 4 component float vectors.
float4 _RadialBiasPerspInMeters[DK_MAX_NUM_PERSPECTIVES];

#endif //_DEPTHKIT_CORE_UNIFORMS_CGINC