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

    [RequireComponent(typeof(Depthkit.MeshSource))]
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public abstract class VFXTextureSource : Depthkit.DataSource, IPropertyTransfer
    {
        #region Properties
        protected static class VFXTextureSourceShaderIds
        {
            public static readonly int
                _TextureSizeReductionFactor = Shader.PropertyToID("_TextureSizeReductionFactor"),
                _PositionData = Shader.PropertyToID("_PositionData"),
                _ColorData = Shader.PropertyToID("_ColorData"),
                _NormalData = Shader.PropertyToID("_NormalData");
        }

        [HideInInspector]
        public RenderTexture positionDataTexture;

        [HideInInspector]
        public RenderTexture colorDataTexture;

        [HideInInspector]
        public RenderTexture normalDataTexture;

        [Min(1)]
        public int textureSizeReductionFactor = 1;

        public bool usePowerOf2Size = false;

        [HideInInspector]
        public Vector2Int dataTextureSize { get; protected set; } = Vector2Int.zero;

        protected ComputeShader m_generateDataCompute;

        protected int m_copyTexturesKernelId = -1;

        public abstract MeshSource meshSource
        {
            get;
        }

        #endregion

        #region TextureSource
        protected abstract void SetMeshSource();

        public abstract string GetComputeShaderName();

        public abstract string GetComputeKernelBaseName();

        internal void CreatePositionDataTexture()
        {
            if (positionDataTexture != null && positionDataTexture.IsCreated())
            {
                positionDataTexture.Release();
            }
            positionDataTexture = new RenderTexture(dataTextureSize.x, dataTextureSize.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            positionDataTexture.enableRandomWrite = true;
            positionDataTexture.filterMode = FilterMode.Point;
            positionDataTexture.name = "Depthkit VFX Positions Texture";
            positionDataTexture.Create();
        }

        internal void CreateNormalDataTexture()
        {
            if (normalDataTexture != null && normalDataTexture.IsCreated())
            {
                normalDataTexture.Release();
            }
            normalDataTexture = new RenderTexture(dataTextureSize.x, dataTextureSize.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            normalDataTexture.enableRandomWrite = true;
            normalDataTexture.filterMode = FilterMode.Point;
            normalDataTexture.name = "Depthkit VFX Normals Texture";
            normalDataTexture.Create();
        }

        internal void CreateColorDataTexture()
        {
            if (colorDataTexture != null && colorDataTexture.IsCreated())
            {
                colorDataTexture.Release();
            }
            colorDataTexture = new RenderTexture(dataTextureSize.x, dataTextureSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            colorDataTexture.enableRandomWrite = true;
            colorDataTexture.filterMode = FilterMode.Point;
            colorDataTexture.name = "Depthkit VFX Colors Texture";
            colorDataTexture.Create();
        }

        protected override void OnAwake()
        {
            SetMeshSource();
        }

        protected abstract void SetCommonProperties(ref ComputeShader compute, int kernel);

        #endregion

        #region DataSource
        public override string DataSourceName()
        {
            return "Depthkit Core VFX Texture Source";
        }

        protected override void AcquireResources()
        {
            if (IsSetup() && dataTextureSize.x != 0 && dataTextureSize.y != 0)
            {
                CreatePositionDataTexture();
                CreateColorDataTexture();
                CreateNormalDataTexture();
            }
        }

        protected override void FreeResources()
        {
            if (positionDataTexture != null && positionDataTexture.IsCreated())
            {
                positionDataTexture.Release();
                positionDataTexture = null;
            }
            if (normalDataTexture != null && normalDataTexture.IsCreated())
            {
                normalDataTexture.Release();
                normalDataTexture = null;
            }
            if (colorDataTexture != null && colorDataTexture.IsCreated())
            {
                colorDataTexture.Release();
                colorDataTexture = null;
            }
        }

        public override bool OnSetup()
        {
           if(m_generateDataCompute == null){
                m_generateDataCompute = Resources.Load(GetComputeShaderName(), typeof(ComputeShader)) as ComputeShader;
                if(m_generateDataCompute == null)
                {
                    Debug.LogError("unable to load compute shader: " + GetComputeShaderName());
                    return false;
                }
            }

            m_copyTexturesKernelId = m_generateDataCompute.FindKernel(Util.GetScaled2DKernelName(GetComputeKernelBaseName()));

            return true;
        }

        protected override bool OnResize()
        {   
            if (usePowerOf2Size)
            {
                dataTextureSize = new Vector2Int(Mathf.NextPowerOfTwo(dataTextureSize.x), Mathf.NextPowerOfTwo(dataTextureSize.y));
            }

            CreatePositionDataTexture();
            CreateColorDataTexture();
            CreateNormalDataTexture();

            return true;
        }

        protected override bool OnGenerate()
        {
            if(meshSource.triangleBuffer == null)
            {
                Debug.LogWarning("Couldn't set shader properties: triangles buffer is null");
                return false;
            }

            if(m_copyTexturesKernelId == -1)
            {
                Debug.LogError("Couldn't find kernel: " + GetComputeKernelBaseName());
                return false;
            }

            //copy triangles
            clip.SetProperties(ref m_generateDataCompute, m_copyTexturesKernelId);
            SetProperties(ref m_generateDataCompute, m_copyTexturesKernelId);
            SetCommonProperties(ref m_generateDataCompute, m_copyTexturesKernelId);
            Depthkit.Util.DispatchGroups(m_generateDataCompute, m_copyTexturesKernelId, dataTextureSize.x, dataTextureSize.y, 1);

            return true;
        }

        #endregion

        #region IPropertyTransfer

        public virtual void SetProperties(ref ComputeShader compute, int kernel)
        {
            compute.SetInt(VFXTextureSourceShaderIds._TextureSizeReductionFactor, textureSizeReductionFactor);
            compute.SetTexture(kernel, VFXTextureSourceShaderIds._ColorData, colorDataTexture);
            compute.SetTexture(kernel, VFXTextureSourceShaderIds._PositionData, positionDataTexture);
            compute.SetTexture(kernel, VFXTextureSourceShaderIds._NormalData, normalDataTexture);
            meshSource.SetProperties(ref compute, kernel);
        }

        public virtual void SetProperties(ref Material material)
        {
            material.SetInt(VFXTextureSourceShaderIds._TextureSizeReductionFactor, textureSizeReductionFactor);
            material.SetTexture(VFXTextureSourceShaderIds._ColorData, colorDataTexture);
            material.SetTexture(VFXTextureSourceShaderIds._PositionData, positionDataTexture);
            material.SetTexture(VFXTextureSourceShaderIds._NormalData, normalDataTexture);
            meshSource.SetProperties(ref material);
        }

        public virtual void SetProperties(ref Material material, ref MaterialPropertyBlock block)
        {
            block.SetInt(VFXTextureSourceShaderIds._TextureSizeReductionFactor, textureSizeReductionFactor);
            block.SetTexture(VFXTextureSourceShaderIds._ColorData, colorDataTexture);
            block.SetTexture(VFXTextureSourceShaderIds._PositionData, positionDataTexture);
            block.SetTexture(VFXTextureSourceShaderIds._NormalData, normalDataTexture);
            meshSource.SetProperties(ref material, ref block);
        }

        #endregion
    }
}