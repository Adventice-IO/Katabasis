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
using System;

namespace Depthkit
{
    [CustomEditor(typeof(Depthkit.StudioLiteMeshSource))]
    public class StudioLiteMeshSourceEditor : Editor
    {
        private bool m_showAdvanced = false;

        SerializedProperty meshDensity;
        SerializedProperty adjustableNormalSlope;
        SerializedProperty normalGenerationTechnique;
        SerializedProperty pauseDataGenerationWhenInvisible;
        SerializedProperty pausePlayerWhenInvisible;
        SerializedProperty radialBias;

        MaskGeneratorGUI m_maskGUI;

        void OnEnable()
        {
            meshDensity = serializedObject.FindProperty("m_meshDensity");
            normalGenerationTechnique = serializedObject.FindProperty("normalGenerationTechnique");
            adjustableNormalSlope = serializedObject.FindProperty("adjustableNormalSlope");
            pauseDataGenerationWhenInvisible = serializedObject.FindProperty("pauseDataGenerationWhenInvisible");
            pausePlayerWhenInvisible = serializedObject.FindProperty("pausePlayerWhenInvisible");
            radialBias = serializedObject.FindProperty("radialBias");

            if(m_maskGUI == null)
            {
                m_maskGUI = new MaskGeneratorGUI();
            }
        }

        private void OnDisable()
        {
            m_maskGUI?.Release();
        }

        public override void OnInspectorGUI()
        {
            StudioLiteMeshSource meshSource = target as Depthkit.StudioLiteMeshSource;
            CoreMeshSource coreMeshSource = meshSource;
            bool doGenerate = false;
            bool doResize = false;

            serializedObject.Update();

            if (meshSource.clip == null) 
                return;

            EditorGUI.BeginChangeCheck();

            meshSource.maxPerspectivesToRender = EditorGUILayout.IntSlider("Perspective Limit", meshSource.maxPerspectivesToRender, 1, meshSource.clip.metadata.perspectivesCount);

            EditorGUILayout.PropertyField(normalGenerationTechnique, CoreMeshSourceEditor.s_normalGenTechniqueLabel);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(meshSource);
                doResize = true;
                doGenerate = true;
            }

            NormalGenerationTechnique normalTechnique = (NormalGenerationTechnique)normalGenerationTechnique.enumValueIndex;

            if (normalTechnique == NormalGenerationTechnique.Adjustable ||
                normalTechnique == NormalGenerationTechnique.AdjustableSmoother)
            {
                EditorGUILayout.PropertyField(adjustableNormalSlope, CoreMeshSourceEditor.s_normalSlopeLabel);
                if (GUI.changed)
                {
                    doGenerate = true;
                    GUI.changed = false;
                }
            }
            EditorGUI.BeginChangeCheck();

            CoreMeshSourceEditor.MeshSettingsGUI(ref coreMeshSource, ref doResize, ref doGenerate);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(meshSource);
                doResize = true;
                doGenerate = true;
            }
            EditorGUI.BeginChangeCheck();

            bool adaptiveThreshold = false;

            adaptiveThreshold = meshSource.enableAdaptiveThreshold;
            adaptiveThreshold = EditorGUILayout.Toggle("Enable Adaptive Clip Thresholding", adaptiveThreshold);
            if (adaptiveThreshold != meshSource.enableAdaptiveThreshold)
            {
                meshSource.enableAdaptiveThreshold = adaptiveThreshold;
            }

            if (adaptiveThreshold)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Clipping Threshold");
                float validate;
                validate = EditorGUILayout.FloatField(float.Parse(meshSource.minClipThreshold.ToString("F3")));
                if (validate > 0.001 && validate < meshSource.clipThreshold)
                {
                    meshSource.minClipThreshold = validate;
                }
                EditorGUILayout.MinMaxSlider(ref meshSource.minClipThreshold, ref meshSource.clipThreshold, 0.001f, 1.0f);
                validate = EditorGUILayout.FloatField(float.Parse(meshSource.clipThreshold.ToString("F3")));
                if (validate > meshSource.minClipThreshold && validate <= 1.0)
                {
                    meshSource.clipThreshold = validate;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Dithering Width");
                validate = EditorGUILayout.FloatField(float.Parse(meshSource.minDitherWidth.ToString("F3")));
                if (validate > 0.0 && validate < meshSource.ditherWidth)
                {
                    meshSource.minDitherWidth = validate;
                }
                EditorGUILayout.MinMaxSlider(ref meshSource.minDitherWidth, ref meshSource.ditherWidth, 0.0f, 0.2f);
                validate = EditorGUILayout.FloatField(float.Parse(meshSource.ditherWidth.ToString("F3")));
                if (validate > meshSource.minDitherWidth && validate <= 0.2)
                {
                    meshSource.ditherWidth = validate;
                }
                EditorGUILayout.EndHorizontal();

                float angleInDegrees = Mathf.Acos(meshSource.maxViewAngleCosThreshold) * 180.0f / Mathf.PI;
                angleInDegrees = EditorGUILayout.Slider("Max View Angle Threshold", angleInDegrees, 0.0f, 90.0f);
                meshSource.maxViewAngleCosThreshold = Mathf.Cos(angleInDegrees * Mathf.PI / 180.0f);
            }
            else
            {
                meshSource.clipThreshold = EditorGUILayout.Slider("Clipping Threshold", meshSource.clipThreshold, 0.0f, 1.0f);
                meshSource.ditherWidth = EditorGUILayout.Slider("Dithering Width", meshSource.ditherWidth, 0.0f, 0.2f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(meshSource);
                doGenerate = true;
            }

            EditorGUI.BeginChangeCheck();
            m_maskGUI.MaskGui(ref meshSource.maskGenerator, meshSource.meshDensity, ref doGenerate);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(meshSource);
            }

            EditorGUI.BeginChangeCheck();


            m_showAdvanced = EditorGUILayout.Foldout(m_showAdvanced, "Advanced Settings");
            if (m_showAdvanced)
            {
                EditorGUILayout.PropertyField(radialBias, new GUIContent("Depth Bias Compensation", "Time of Flight cameras measure surfaces farther away than they are in reality. The amount of bias depends greatly on the material of the surface being measured. Skin in particular has a large bias. The Depth Bias Compensation is a correction for this error by pulling the surface back towards their true depth. It most useful for recovering high quality faces and hands on otherwise well-calibrated captures. The larger the value, the larger the compensation. 0 means no depth bias compensation is applied."));
                meshSource.edgeCompressionNoiseThreshold = EditorGUILayout.Slider("Edge Compression Noise Threshold", meshSource.edgeCompressionNoiseThreshold, 0.0f, 1.0f);
                EditorGUILayout.PropertyField(pausePlayerWhenInvisible);
                EditorGUILayout.PropertyField(pauseDataGenerationWhenInvisible);
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(meshSource);
                doResize = true;
                doGenerate = true;
            }

            if (meshSource.transform.hasChanged)
            {
                doGenerate = true;
                meshSource.transform.hasChanged = false;
            }

            serializedObject.ApplyModifiedProperties();

            if(doResize)
            {
                meshSource.Resize();
            }
            if(doGenerate)
            {
                m_maskGUI.MarkDirty();
                meshSource.Generate();
            }
        }
    }
}