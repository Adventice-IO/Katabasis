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
using UnityEditor;
using System.Runtime.InteropServices;

namespace Depthkit
{
    //data textures are one dataset per triangle, averaged positions, normals and uvs
    [RequireComponent(typeof(Depthkit.CoreMeshSource))]
    [AddComponentMenu("Depthkit/Core/Sources/Depthkit Core Visual Effect Graph Texture Source")]
    public class CoreVFXTextureSource : VFXTextureSource
    {
        #region Properties
        protected static class CoreVFXTextureSourceShaderIds
        {
            public static readonly int
                _VertexBufferDimensions = Shader.PropertyToID("_VertexBufferDimensions"),
                _DataTextureSize = Shader.PropertyToID("_DataTextureSize");
        }

        [HideInInspector]
        public Vector2 vertexBufferDimensions { get; private set; } = Vector2.zero;

        private Depthkit.CoreMeshSource m_meshSource;

        public override MeshSource meshSource
        {
            get { return m_meshSource; }
        }
        #endregion

        #region TextureSource
        public override string GetComputeShaderName()
        {
            return "Shaders/DataSource/CopyCoreMeshToTextures";
        }

        public override string GetComputeKernelBaseName()
        {
            return "CopyCoreMeshToTextures";
        }

        protected override void SetMeshSource()
        {
            m_meshSource = GetComponent<Depthkit.CoreMeshSource>();
        }

        protected override void SetCommonProperties(ref ComputeShader compute, int kernel)
        {
            compute.SetVector(CoreVFXTextureSourceShaderIds._VertexBufferDimensions, vertexBufferDimensions);
            compute.SetVector(CoreVFXTextureSourceShaderIds._DataTextureSize, new Vector2(dataTextureSize.x, dataTextureSize.y));
        }

        #endregion

        #region DataSource
        public override string DataSourceName()
        {
            return "Depthkit Core VFX Texture Source";
        }

        protected override bool OnResize()
        {   
            if(m_meshSource == null)
            {
                Debug.Log("Failed to resize: core mesh source is null");
                return false;
            }

            vertexBufferDimensions = m_meshSource.latticeResolution;
            dataTextureSize = m_meshSource.latticeResolution / textureSizeReductionFactor;

            return base.OnResize();
        }
        #endregion

        #region IPropertyTransfer
        public override void SetProperties(ref ComputeShader compute, int kernel)
        {
            compute.SetVector(CoreVFXTextureSourceShaderIds._VertexBufferDimensions, vertexBufferDimensions);
            compute.SetVector(CoreVFXTextureSourceShaderIds._DataTextureSize, new Vector2(dataTextureSize.x, dataTextureSize.y));
            base.SetProperties(ref compute, kernel);
        }

        public override void SetProperties(ref Material material)
        {
            material.SetVector(CoreVFXTextureSourceShaderIds._VertexBufferDimensions, vertexBufferDimensions);
            material.SetVector(CoreVFXTextureSourceShaderIds._DataTextureSize, new Vector2(dataTextureSize.x, dataTextureSize.y));
            base.SetProperties(ref material);
        }

        public override void SetProperties(ref Material material, ref MaterialPropertyBlock block)
        {
            block.SetVector(CoreVFXTextureSourceShaderIds._VertexBufferDimensions, vertexBufferDimensions);
            block.SetVector(CoreVFXTextureSourceShaderIds._DataTextureSize, new Vector2(dataTextureSize.x, dataTextureSize.y));
            base.SetProperties(ref material, ref block);
        }
        #endregion
    }
}