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

using UnityEngine;
using UnityEngine.Rendering;

namespace Depthkit
{
    public abstract class ProceduralLook : Look
    {
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        public bool receiveShadows = true;
        public bool interpolateLightProbes = true;
        public Transform anchorOverride = null;
        public Material lookMaterial = null;

        protected override bool UsesMaterial() { return true; }
        protected override Material GetMaterial() { return lookMaterial; }

        protected override bool UsesMaterialPropertyBlock() { return true; }

        protected override void SetMaterialProperties(ref Material material, ref MaterialPropertyBlock block)
        {
            if (interpolateLightProbes)
            {
                Vector3[] positions = new Vector3[1] { anchorOverride != null ? anchorOverride.position : transform.position };
                SphericalHarmonicsL2[] harmonics = new SphericalHarmonicsL2[1];
                Vector4[] occlusions = new Vector4[1];

                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, harmonics, occlusions);

                block.CopyProbeOcclusionArrayFrom(occlusions, 0, 0, 1);
                block.CopySHCoefficientArraysFrom(harmonics, 0, 0, 1);
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (meshSource.triangleBufferDrawIndirectArgs == null ||
                !meshSource.triangleBufferDrawIndirectArgs.IsValid() ||
                meshSource.triangleBuffer == null ||
                !meshSource.triangleBuffer.IsValid())
            {
                return;
            }

            Graphics.DrawProceduralIndirect(
                GetMaterial(),
                meshSource.GetWorldBounds(),
                MeshTopology.Triangles,
                meshSource.triangleBufferDrawIndirectArgs, 0,
                null,
                GetMaterialPropertyBlock(),
                shadowCastingMode, //cast shadows
                receiveShadows, //receive shadows
                gameObject.layer);
        }
    }
}