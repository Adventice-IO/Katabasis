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

DK_EDGEMASK_UNIFORMS

float4x4 _LocalTransform;

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    uint id : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    V2F_SHADOW_CASTER;
#ifndef DK_CORE_SKIP_FRAGMENT_DEPTHSAMPLE
    float2 packedUV : TEXCOORD1;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};
    
v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    Vertex vert = dkSampleTriangleBuffer(floor(v.id / 3), v.id % 3);

#ifndef DK_CORE_SKIP_FRAGMENT_DEPTHSAMPLE
    o.packedUV = vert.uv.zw;
#endif

    v.vertex = mul(_LocalTransform, float4(vert.position, 1));
    v.normal = mul((float3x3) _LocalTransform, vert.normal);
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)

    return o;
}
    
float4 frag(v2f i) : COLOR
{
    
#ifndef DK_CORE_SKIP_FRAGMENT_DEPTHSAMPLE
    uint perspectiveIndex;
    float2 depthUV, colorUV, perspectiveUV;
    dkUnpackUVs(i.packedUV, colorUV, depthUV, perspectiveUV, perspectiveIndex);
    float depth = dkSampleDepth(depthUV, perspectiveIndex, perspectiveUV);
    float alpha = dkValidateNormalizedDepth(perspectiveIndex, depth) ? DK_SAMPLE_EDGEMASK(perspectiveUV, perspectiveIndex, i.pos) : -1.f;
    DK_FRAGMENT_CLIP(alpha, perspectiveIndex)
#endif
    
    SHADOW_CASTER_FRAGMENT(i)
}