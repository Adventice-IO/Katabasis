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

using UnityEditor;
using UnityEngine;

namespace Depthkit
{
    public class MaskGeneratorGUI
    {
        RenderTexture[] m_arrayTextures = null;
        RenderTexture[] m_arrayDownscaledTextures = null;
        bool m_filled = false;
        float m_scale = 0.5f;
        private static MeshDensity s_meshDensity;

        private bool m_showTextures = false;

        public static readonly GUIContent s_maskScaleLabel = new GUIContent("Down Scale", "Adjust the scale of the mask. Higher values capture more detail and have cleaner edges at the cost of performance.");
        public static readonly GUIContent s_edgeMaskSobelStrengthLabel = new GUIContent("Intensity", "Corrects texture spill between surfaces, such as fragments of hands appearing on clothing.");
        public static readonly GUIContent s_edgeMaskBlurRadiusLabel = new GUIContent("Feather", "Softens texture spill correction. Needs to be adjusted visually and varies based on the Texture Spill Correction Intensity applied above.");
        public static readonly GUIContent s_previewMaskOnGeoLabel = new GUIContent("Debug Edge Mask", "Debug view of the mask generated per perspective for precise edge masking adjustments.");

        public static bool ValidateMeshDensity(System.Enum md)
        {
            return s_meshDensity >= (MeshDensity)md;
        }

        public void MaskGui(ref MaskGenerator maskGenerator, MeshDensity meshDensity, ref bool doGenerate, bool drawBox=false)
        {
            if (drawBox)
            {
                EditorGUILayout.BeginVertical("Box");
            }
            else
            {
                EditorGUILayout.Space();
                EditorUtil.DrawUILine(Color.gray, 1);
            }

            EditorGUI.BeginChangeCheck();

            bool previewMaskOnGeo = maskGenerator.enableMaskDebug;
            previewMaskOnGeo = EditorGUILayout.Toggle(s_previewMaskOnGeoLabel, previewMaskOnGeo);
            if (previewMaskOnGeo != maskGenerator.enableMaskDebug)
            {
                maskGenerator.enableMaskDebug = previewMaskOnGeo;
            }

            s_meshDensity = meshDensity;
            MeshDensity d = (MeshDensity)EditorGUILayout.EnumPopup(s_maskScaleLabel, (MeshDensity)maskGenerator.scale, ValidateMeshDensity, false);
            if (maskGenerator.scale != (int)d)
            {
                maskGenerator.scale = (int)d;
            }

            EditorGUILayout.LabelField("Texture Spill Correction", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("Box");
            maskGenerator.sobelMultiplier = EditorGUILayout.Slider(s_edgeMaskSobelStrengthLabel, maskGenerator.sobelMultiplier, 0.0f, 100.0f);
            maskGenerator.blurRadius = EditorGUILayout.Slider(s_edgeMaskBlurRadiusLabel, maskGenerator.blurRadius, 0.0f, 0.1f);
            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                doGenerate = true;
            }

            PreviewGUI(ref m_showTextures, ref maskGenerator, false);

            if (drawBox)
            {
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorUtil.DrawUILine(Color.gray, 1);
                EditorGUILayout.Space();
            }
        }

        public void MarkDirty()
        {
            m_filled = false;
        }

        void ReleaseTexture(ref RenderTexture[] textures)
        {
            if (textures != null)
            {
                foreach (var tex in textures)
                {
                    if (tex == null) continue;
                    tex.Release();
                    Object.DestroyImmediate(tex);
                }
            }
            textures = null;
        }

        public void Release()
        {
            ReleaseTexture(ref m_arrayTextures);
            ReleaseTexture(ref m_arrayDownscaledTextures);
        }

        public void PreviewGUI(ref bool show, ref MaskGenerator maskGenerator, bool showDownscaled)
        {
            show = EditorGUILayout.Foldout(show, "Edge Mask Preview");
            if (show)
            {
                if (maskGenerator.maskTexture != null)
                {
                    m_scale = EditorGUILayout.Slider("Preview Scale", m_scale, 0.0001f, 1.0f);
                    GUI.enabled = false;
                    EditorGUILayout.Vector2Field("Mask Dimensions", new Vector2(maskGenerator.maskTexture.width, maskGenerator.maskTexture.height));
                    GUI.enabled = true;
                    switch (maskGenerator.maskTexture.dimension)
                    {
                        case UnityEngine.Rendering.TextureDimension.Tex2D:
                            GUILayout.Label(maskGenerator.maskTexture, GUILayout.Height(maskGenerator.maskTexture.height * m_scale));
                            break;
                        case UnityEngine.Rendering.TextureDimension.Tex2DArray:
                            {
                                if (!m_filled)
                                {
                                    // Mask
                                    if (m_arrayTextures == null || m_arrayTextures.Length != maskGenerator.maskTexture.volumeDepth)
                                    {
                                        if (m_arrayTextures != null)
                                        {
                                            foreach (var tex in m_arrayTextures)
                                            {
                                                tex.Release();
                                                Object.DestroyImmediate(tex);
                                            }
                                        }
                                        m_arrayTextures = new RenderTexture[maskGenerator.maskTexture.volumeDepth];
                                    }
                                    for (int i = 0; i < maskGenerator.maskTexture.volumeDepth; ++i)
                                    {
                                        RenderTexture tempTexture = new RenderTexture(maskGenerator.maskTexture.width, maskGenerator.maskTexture.height, 0, maskGenerator.maskTexture.format, 0);
                                        tempTexture.Create();
                                        Graphics.CopyTexture(maskGenerator.maskTexture, i, tempTexture, 0);
                                        if (m_arrayTextures[i] != null)
                                        {
                                            m_arrayTextures[i].Release();
                                        }
                                        m_arrayTextures[i] = tempTexture;
                                    }
                                    if (showDownscaled)
                                    {
                                        // Downscaled Mask
                                        if (m_arrayDownscaledTextures == null || m_arrayDownscaledTextures.Length != maskGenerator.downScaledMaskTexture.volumeDepth)
                                        {
                                            if (m_arrayDownscaledTextures != null)
                                            {
                                                foreach (var tex in m_arrayDownscaledTextures)
                                                {
                                                    tex.Release();
                                                    Object.DestroyImmediate(tex);
                                                }
                                            }
                                            m_arrayDownscaledTextures = new RenderTexture[maskGenerator.downScaledMaskTexture.volumeDepth];
                                        }
                                        for (int i = 0; i < maskGenerator.downScaledMaskTexture.volumeDepth; ++i)
                                        {
                                            RenderTexture tempTexture = new RenderTexture(maskGenerator.downScaledMaskTexture.width, maskGenerator.downScaledMaskTexture.height, 0, maskGenerator.downScaledMaskTexture.format, 0);
                                            tempTexture.Create();
                                            Graphics.CopyTexture(maskGenerator.downScaledMaskTexture, i, tempTexture, 0);
                                            if (m_arrayDownscaledTextures[i] != null)
                                            {
                                                m_arrayDownscaledTextures[i].Release();
                                            }
                                            m_arrayDownscaledTextures[i] = tempTexture;
                                        }
                                    }

                                    m_filled = true;
                                }
                                for (int i = 0; i < m_arrayTextures.Length; ++i)
                                {
                                    GUILayout.Label(m_arrayTextures[i], GUILayout.Height(maskGenerator.maskTexture.height * m_scale));
                                    if (showDownscaled) GUILayout.Label(m_arrayDownscaledTextures[i], GUILayout.Height(maskGenerator.downScaledMaskTexture.height * m_scale));
                                }
                            }
                            break;
                        default: break;
                    }
                }
            }
            else
            {
                m_filled = false;
            }
        }
    }
}