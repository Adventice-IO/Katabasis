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

#ifndef _DK_CORE_TRIANGLE_TYPES_CGINC
#define _DK_CORE_TRIANGLE_TYPES_CGINC

struct Triangle
{
#ifndef DK_CORE_PACKED_TRIANGLE
    uint perspectiveIndex;
    uint vertex[3];
#else 
    Vertex vertex[3];
#endif
};

Triangle newTriangle()
{
    Triangle t;
#ifndef DK_CORE_PACKED_TRIANGLE
    t.perspectiveIndex = 0;
    t.vertex[0] = 0;
    t.vertex[1] = 0;
    t.vertex[2] = 0;
#else 
    t.vertex[0] = newVertex();
    t.vertex[1] = newVertex();
    t.vertex[2] = newVertex();
#endif
    return t;
}

#endif