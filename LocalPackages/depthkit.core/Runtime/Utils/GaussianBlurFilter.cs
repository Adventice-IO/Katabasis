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

using Depthkit;
using System;
using UnityEngine;

namespace Depthkit
{
    [System.Serializable]
    public class GaussianBlurFilter
    {
        public Clip clip;

        [Range(0.0f, 0.25f)]
        public float radius = Metadata.DefaultReconstructionSettingsDefaults.edgeMaskBlurAmount;

        public int slices = 1;

        protected RenderTexture[] m_blurTextures = null;
        protected ComputeShader m_blurCompute;
        protected ComputeBuffer m_blurRadiusBuffer = null;

        protected static string s_defaultComputeBlurShaderName = "Shaders/Util/MipMapGaussianBlur";

        internal static class BlurShaderIds
        {
            internal static readonly int
                _InputTexture = Shader.PropertyToID("_InputTexture"),
                _OutputTexture = Shader.PropertyToID("_OutputTexture"),
                _Slice = Shader.PropertyToID("_Slice"),
                _Axis = Shader.PropertyToID("_Axis"),
                _MinDim = Shader.PropertyToID("_MinDim"),
                _BlurWidth = Shader.PropertyToID("_BlurWidth");
        }

        public bool hasTexture
        {
            get
            {
                return m_blurTextures != null && m_blurTextures.Length == 2;
            }
        }

        RenderTexture InitBlurTexture(RenderTexture tex)
        {
            RenderTexture newTex = new RenderTexture(Mathf.NextPowerOfTwo(tex.width), Mathf.NextPowerOfTwo(tex.height), tex.depth, tex.format);
            newTex.dimension = tex.dimension;
            newTex.volumeDepth = tex.volumeDepth;
            newTex.filterMode = FilterMode.Trilinear;
            newTex.name = tex.name + " blur";
            newTex.enableRandomWrite = true;
            newTex.autoGenerateMips = false;
            newTex.useMipMap = true;
            newTex.Create();
            newTex.GenerateMips();
            return newTex;
        }

        void CreateTextures(RenderTexture tex)
        {
            if (m_blurTextures == null || m_blurTextures.Length != 2)
            {
                m_blurTextures = new RenderTexture[2];
            }

            if (m_blurTextures[0] != null && m_blurTextures[0].IsCreated())
            {
                m_blurTextures[0].Release();
            }

            m_blurTextures[0] = InitBlurTexture(tex);

            if (m_blurTextures[1] != null && m_blurTextures[1].IsCreated())
            {
                m_blurTextures[1].Release();
            }

            m_blurTextures[1] = InitBlurTexture(tex);
        }

        public void Setup(string computeShader = "")
        {
            Util.EnsureComputeShader(ref m_blurCompute, computeShader == string.Empty ? s_defaultComputeBlurShaderName : computeShader);
        }

        public virtual void EnsureTextures(RenderTexture tex)
        {
            if (m_blurTextures == null ||
                m_blurTextures.Length != 2 ||
                m_blurTextures[0].width != Mathf.NextPowerOfTwo(tex.width) ||
                m_blurTextures[0].height != Mathf.NextPowerOfTwo(tex.height) ||
                m_blurTextures[0].dimension != tex.dimension ||
                m_blurTextures[0].volumeDepth != tex.volumeDepth)
            {
                CreateTextures(tex);
            }
        }

        public virtual void DoBlur(RenderTexture tex)
        {
            EnsureTextures(tex);

            // compute new sobel strength based on scale differences to original texture size
            Util.EnsureComputeBuffer(ComputeBufferType.Structured, ref m_blurRadiusBuffer, slices, sizeof(float));

            float[] radii = new float[slices];

            for (int i = 0; i < slices; i++)
            {
                // copy src texture to be blurred into the horizontal input and generate mipmaps
                Graphics.CopyTexture(tex, i, 0, 0, 0, tex.width, tex.height, m_blurTextures[0], i, 0, 0, 0);

                // compute the radius for each perspective based on the size differences from the original
                ref Metadata.Perspective perspective = ref clip.metadata.perspectives[i];
                float minOriginalDim = Mathf.Min(perspective.depthImageSize.x, perspective.depthImageSize.y);
                Vector2 croppedDim = perspective.depthImageSize * new Vector2(perspective.crop.z, perspective.crop.w);
                float maxCroppedDim = Mathf.Max(croppedDim.x, croppedDim.y);

                radii[i] = radius * minOriginalDim / maxCroppedDim;
            }

            m_blurTextures[0].GenerateMips();

            m_blurRadiusBuffer.SetData(radii);
            m_blurCompute.SetBuffer(0, BlurShaderIds._BlurWidth, m_blurRadiusBuffer);

            // horizontal
            m_blurCompute.SetTexture(0, BlurShaderIds._InputTexture, m_blurTextures[0]);
            m_blurCompute.SetTexture(0, BlurShaderIds._OutputTexture, m_blurTextures[1]);
            m_blurCompute.SetFloat(BlurShaderIds._MinDim, Math.Min(tex.width, tex.height));
            m_blurCompute.SetVector(BlurShaderIds._Axis, new Vector2(1, 0));

            Util.DispatchGroups(m_blurCompute, 0, m_blurTextures[0].width, m_blurTextures[0].height, slices);

            m_blurTextures[1].GenerateMips();

            // vertical
            m_blurCompute.SetTexture(0, BlurShaderIds._InputTexture, m_blurTextures[1]);
            m_blurCompute.SetTexture(0, BlurShaderIds._OutputTexture, tex);
            m_blurCompute.SetVector(BlurShaderIds._Axis, new Vector2(0, 1));

            Util.DispatchGroups(m_blurCompute, 0, tex.width, tex.height, slices);
        }

        public void Release()
        {
            Util.ReleaseComputeBuffer(ref m_blurRadiusBuffer);
            if (m_blurTextures != null)
            {
                //release the copy texture
                foreach (RenderTexture tex in m_blurTextures)
                {
                    if (tex != null && tex.IsCreated())
                    {
                        tex.Release();
                    }
                }
                m_blurTextures = null;
            }
        }
    }
}
