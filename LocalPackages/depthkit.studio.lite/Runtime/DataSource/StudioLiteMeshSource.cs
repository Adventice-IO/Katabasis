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
using System;
using System.Collections.Generic;
using UnityEditor;

namespace Depthkit
{
    [RequireComponent(typeof(Depthkit.Clip))]
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [AddComponentMenu("Depthkit/Studio/Sources/Depthkit Studio Lite Mesh Source")]
    public class StudioLiteMeshSource : CoreMeshSource
    {
        #region Properties
        public static class StudioLiteMeshSourceShaderIds
        {
            public static readonly int
                _EdgeTriangleBuffer = Shader.PropertyToID("_EdgeTriangleBuffer"),
                _MainPerspectiveIndex = Shader.PropertyToID("_MainPerspectiveIndex");
        }

        public Transform volumeViewpoint;
        public int maxPerspectivesToRender = 3;

        public bool enableAdaptiveThreshold = false;
        [Range(-1.0f, 1.0f)]
        public float maxViewAngleCosThreshold = 0.70710678118f; // cos(45)

        public enum SubMeshIndex
        {
            Main = 0,
            Fill = 1,
            MainEdges = 2,
            FillEdges = 3,
            Count = 4
        }

        [Range(0.001f, 1.0f)]
        public float minClipThreshold = 0.001f;

        [Range(0.0f, 0.2f)]
        public float minDitherWidth = 0.0f;
        #endregion

        #region CoreMeshSource

        protected override string GetComputeShaderName()
        {
            return "Shaders/DataSource/StudioLiteMeshSource";
        }

        #endregion

        #region TriangleGeneration

        protected override string GetKernelNamePostfix(string prefix, bool edgeBuffer, bool edgeMask, int submesh)
        {
            return prefix + (submesh != -1 ? ((SubMeshIndex)submesh == SubMeshIndex.Main ? "Main" : "Fill") + "Perspective" : "") + (edgeBuffer ? "WEdgeBuffer" : "") + (edgeMask ? "WEdgeMask" : "");
        }

        protected override void GenerateTriangles()
        {
            currentSubmeshIndex = (uint)SubMeshIndex.Main;

            var main = GetSubMesh((int)SubMeshIndex.Main);
            var mainEdges = GetSubMesh((int)SubMeshIndex.MainEdges);

            //Generate Main Perspective and Edge triangles in one pass
            mainEdges.triangleBuffer.SetCounterValue(0);
            main.triangleBuffer.SetCounterValue(0);

            int kernel = FindKernelId(Phase.Triangles, true, false, (int)SubMeshIndex.Main);

            m_generateDataCompute.SetBuffer(kernel, SubMesh.TriangleDataShaderIds._TriangleBuffer, main.triangleBuffer);
            m_generateDataCompute.SetBuffer(kernel, StudioLiteMeshSourceShaderIds._EdgeTriangleBuffer, mainEdges.triangleBuffer);

            clip.SetProperties(ref m_generateDataCompute, kernel);
            m_generateDataCompute.SetBuffer(kernel, CoreShaderIds._VertexBuffer, vertexBuffer);

            //dispatch only the main slice of the vertex buffer
            Util.DispatchGroups(m_generateDataCompute, kernel, m_latticeResolution.x, m_latticeResolution.y, 1);

            if (maxPerspectivesToRender > 1)
            {
                //generate triangles for the fill perspectives
                currentSubmeshIndex = (int)SubMeshIndex.Fill;

                var fill = GetSubMesh((int)SubMeshIndex.Fill);
                fill.triangleBuffer.SetCounterValue(0);
                var fillEdges = GetSubMesh((int)SubMeshIndex.FillEdges);
                fillEdges.triangleBuffer.SetCounterValue(0);

                kernel = FindKernelId(Phase.Triangles, true, false, (int)SubMeshIndex.Fill);

                clip.SetProperties(ref m_generateDataCompute, kernel);
                m_generateDataCompute.SetBuffer(kernel, CoreShaderIds._VertexBuffer, vertexBuffer);
                m_generateDataCompute.SetBuffer(kernel, SubMesh.TriangleDataShaderIds._TriangleBuffer, fill.triangleBuffer);
                m_generateDataCompute.SetBuffer(kernel, StudioLiteMeshSourceShaderIds._EdgeTriangleBuffer, fillEdges.triangleBuffer);

                if (Application.platform == RuntimePlatform.Android) // Quest 2 has issues with using too much group shared memory
                {
                    m_generateDataCompute.DisableKeyword(CoreShaderIds._UseGroupSharedMemory);
                }
                else
                {
                    m_generateDataCompute.EnableKeyword(CoreShaderIds._UseGroupSharedMemory);
                }

                //dispatch the fill slices of the vertex buffer
                Util.DispatchGroups(m_generateDataCompute, kernel, scaledPerspectiveResolution.x, scaledPerspectiveResolution.y, maxPerspectivesToRender - 1);
            }
        }

        #endregion

        #region DataSource

        public override string DataSourceName()
        {
            return "Depthkit Studio Lite Mesh Source";
        }

        protected override bool OnResize()
        {
            if (m_clip.isSetup)
            {
                ReserveSubMeshes<IndexedCoreTriangleSubMesh>((int)SubMeshIndex.Count);
                m_vertexBufferSlices = maxPerspectivesToRender;
                m_currentSubmeshIndex = (uint)SubMeshIndex.Main;
                vertexCount = 0;
                ResizeLattice();

                //set vertex count to 3D buffer size, one slice per perspective
                vertexCount = (uint)m_latticeResolution.x * (uint)m_latticeResolution.y * (uint)maxPerspectivesToRender;

                for (int i = 0; i < (int)SubMeshIndex.Count; ++i)
                {
                    var subMesh = GetSubMesh(i);

                    if (subMesh.maxTriangles == 0 || !subMesh.useTriangleMesh)
                    {
                        //single perspective size
                        subMesh.maxTriangles = m_latticeMaxTriangles;

                        //fill renders all other perspectives at once
                        if (i == (int)SubMeshIndex.Fill || i == (int)SubMeshIndex.FillEdges)
                        {
                            subMesh.maxTriangles *= (uint)maxPerspectivesToRender - 1;
                        }

                        //Edges have less triangles so set the max triangles to half of what the lattice would use
                        if (i == (int)SubMeshIndex.MainEdges || i == (int)SubMeshIndex.FillEdges)
                        {
                            subMesh.maxTriangles /= 2;
                        }
                    }

                    subMesh.Init(i);
                }
                EnsureVertexBuffer();

                EnsureMaskGenerator();

                maskGenerator.clip = clip;
                maskGenerator.sliceCount = maxPerspectivesToRender;

                return base.baseResize();
            }
            return false;
        }

        protected override void GenerateEdgeMask()
        {
            maskGenerator.GenerateMask();
            //generate down scaled tex to match meshDensity
            maskGenerator.downScale = (int)meshDensity;
        }

        protected override bool OnGenerate()
        {
            if (volumeViewpoint == null && Camera.main != null)
            {
                volumeViewpoint = Camera.main.transform; //use main camera by default
            }

            // find the bounding box of all sensors to constrain the view position and clip center within
            Bounds cameraBounds = new Bounds(clip.metadata.perspectives[0].cameraCenter, new Vector3(0.0f, 0.0f, 0.0f));
            for (int perspectiveIndex = 1; perspectiveIndex < clip.metadata.perspectivesCount; ++perspectiveIndex)
            {
                cameraBounds.Encapsulate(clip.metadata.perspectives[perspectiveIndex].cameraCenter);
            }

            Vector3 localViewpoint = transform.InverseTransformPoint(volumeViewpoint.position);

            // Constrain to cameraBounds
            localViewpoint = Vector3.Max(Vector3.Min(localViewpoint, cameraBounds.max), cameraBounds.min);

            SortedList<float, int> sortedPerspectives = new SortedList<float, int>();
            for (int perspectiveIndex = 0; perspectiveIndex < clip.metadata.perspectivesCount; ++perspectiveIndex)
            {
                Vector3 cameraNormal = clip.metadata.perspectives[perspectiveIndex].cameraNormal;
                float perspectiveMidDepth = (clip.metadata.perspectives[perspectiveIndex].nearClip + clip.metadata.perspectives[perspectiveIndex].farClip) * 0.5f;
                Vector3 perspectiveCenter = clip.metadata.perspectives[perspectiveIndex].cameraCenter + clip.metadata.perspectives[perspectiveIndex].cameraNormal * perspectiveMidDepth;
                Vector3 viewToCenter = (perspectiveCenter - localViewpoint).normalized;

                float viewAngleCos = Vector3.Dot(viewToCenter, cameraNormal);

                //sort list from most aligned to least aligned
                try
                {
                    sortedPerspectives.Add(-1 * viewAngleCos, perspectiveIndex);
                }
                catch (Exception)
                {
                    //couldn't add if it's already in there.
                }
            }

            EnsureMaskGenerator();

            for (int p = 0; p < maskGenerator.perspectivesToSlice.Length; ++p)
            {
                maskGenerator.perspectivesToSlice[p] = Vector4.zero;
            }

            float viewDependentClipThreshold = clipThreshold;
            float viewDependentDitherWidth = ditherWidth;
            for (int ind = 0; ind < maxPerspectivesToRender; ++ind)
            {
                float viewAngleCos = sortedPerspectives.Keys[ind] * -1.0f;
                int perspectiveIndex = sortedPerspectives.Values[ind];

                if (enableAdaptiveThreshold)
                {
                    float blend = Mathf.Clamp01(Mathf.InverseLerp(1.0f, maxViewAngleCosThreshold, viewAngleCos));
                    blend = Mathf.SmoothStep(0.0f, 1.0f, blend);
                    viewDependentClipThreshold = Mathf.Lerp(minClipThreshold, clipThreshold, blend);
                    viewDependentDitherWidth = Mathf.Lerp(minDitherWidth, ditherWidth, blend);
                }
                //map perspective id to slice index
                maskGenerator.perspectivesToSlice[perspectiveIndex] = new Vector4(ind, viewDependentClipThreshold, viewDependentDitherWidth, 0);
                //map slice index to perspective id
                maskGenerator.sliceToPerspective[ind] = new Vector4(perspectiveIndex, 0, 0, 0);
            }

            m_generateDataCompute.SetInt(MaskGenerator.MaskGeneratorShaderIds._SliceCount, maxPerspectivesToRender);
            m_generateDataCompute.SetVectorArray(MaskGenerator.MaskGeneratorShaderIds._PerspectiveToSlice, maskGenerator.perspectivesToSlice);
            m_generateDataCompute.SetVectorArray(MaskGenerator.MaskGeneratorShaderIds._SliceToPerspective, maskGenerator.sliceToPerspective);
            m_generateDataCompute.SetVector(CoreShaderIds._LatticeSize, new Vector2(m_latticeResolution.x, m_latticeResolution.y));

            return base.OnGenerate();
        }

        #endregion
    }
}