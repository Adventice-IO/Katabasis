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

#ifndef _DK_CORE_VERTEX_TYPE_CGINC
#define _DK_CORE_VERTEX_TYPE_CGINC

//Vertices are in Object Space
struct Vertex
{
    float4 uv; //[xy = perspective, zw = packed]
    float3 position;
    float3 normal;
    uint perspectiveIndex;
    uint validFlag;
};

Vertex newVertex()
{
    Vertex v;
    v.uv = float4(0, 0, 0, 0);
    v.perspectiveIndex = 0;
    v.position = float3(0, 0, 0);
    v.normal = float3(0, 0, 0);
    v.validFlag = 0;
    return v;
}

#endif