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

using System.Collections;
using UnityEngine;

namespace Depthkit
{
    //Transfer properties from a source to a target compute, material or material prop block
    public interface IPropertyTransfer
    {
        void SetProperties(ref ComputeShader compute, int kernel);
        void SetProperties(ref Material material);
        void SetProperties(ref Material material, ref MaterialPropertyBlock block);
    }
}