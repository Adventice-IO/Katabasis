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

#ifndef DK_COREMESHSOURCECOMMON_INC
#define DK_COREMESHSOURCECOMMON_INC

#include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/CoreVertex.cginc"
#include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/CoreTriangle.cginc"
#include "Packages/nyc.scatter.depthkit.core/Runtime/Resources/Shaders/Includes/Depthkit.cginc"

float2 _LatticeSize;
float2 _PerspectiveTextureSize;

AppendStructuredBuffer<Triangle> _TriangleBuffer;

#ifdef DK_VERTEX_BUFFER_READONLY
StructuredBuffer<Vertex> _VertexBuffer;
#else
RWStructuredBuffer<Vertex> _VertexBuffer;
#endif

#ifdef DK_USE_EDGE_TRIANGLES_BUFFER
AppendStructuredBuffer<Triangle> _EdgeTriangleBuffer;
#endif

#ifdef DK_CORE_USE_EDGEMASK
Texture2DArray<float4> _MaskTexture;
SamplerState _Mask_LinearClamp;
#endif
float _MaskClipThreshold;
float4 _PerspectiveToSlice[DK_MAX_NUM_PERSPECTIVES];

float _NormalHeight = 255.0f;

#define BLOCK_SIZE 8

#define INVALID_POSITION float3(0,0,0);

static const uint2 vert_coords[4] =
{
    uint2(0, 0), //tl
    uint2(0, 1), //bl
    uint2(1, 0), //tr
    uint2(1, 1) //br
};

bool vertexOutOfBounds(int3 pixel)
{
    // Note: using > instead of >= here to ensure we have enough verts for one quad per pixel of the CPP.
    // Using >= would omit the top right edge verts
    return pixel.x < 0 || pixel.y < 0 || pixel.x > (int)(_PerspectiveTextureSize.x) || pixel.y > (int)(_PerspectiveTextureSize.y);
}

#ifndef DK_CORE_CHECK_DISPATCH_VALID
#define DK_CORE_CHECK_DISPATCH_VALID
#endif

#ifndef DK_CORE_DATA_SAMPLE_FN
#define DK_CORE_DATA_SAMPLE_FN(dispatchUV, pixel) dispatchUV;
#endif

#ifndef DK_SLICE_OFFSET
#define DK_SLICE_OFFSET(z) z
#endif

#ifndef DK_TO_VERTEX_BUFFER_INDEX 
#define DK_TO_VERTEX_BUFFER_INDEX(coord) toIndexClamp(coord.xy, _LatticeSize)
#endif

//validflags
#define DK_VERTEX_INVALID 0
#define DK_VERTEX_VALID 1
#define DK_VERTEX_LINEAR_MIN_THRESHOLD_VALID 2
#define DK_VERTEX_LINEAR_MAX_THRESHOLD_VALID 4
#define DK_VERTEX_POINT_MIN_THRESHOLD_VALID 8
#define DK_VERTEX_POINT_MAX_THRESHOLD_VALID 16
#define DK_VERTEX_OUT_OF_BOUNDS 32

#endif