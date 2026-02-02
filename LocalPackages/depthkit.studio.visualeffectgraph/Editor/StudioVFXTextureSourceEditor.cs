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
using System.Linq;
using System.Collections.Generic;
using System;

namespace Depthkit
{
    [CustomEditor(typeof(Depthkit.StudioVFXTextureSource))]
    public class StudioVFXTextureSourceEditor : Editor
    {
        SerializedProperty textureSizeReductionFactor;
        SerializedProperty usePowerOf2Size;
        SerializedProperty m_bIsSetup;
        bool m_showTextures = false;

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        void OnEnable()
        {
            m_bIsSetup = serializedObject.FindProperty("m_bIsSetup");
            textureSizeReductionFactor = serializedObject.FindProperty("textureSizeReductionFactor");
            usePowerOf2Size = serializedObject.FindProperty("usePowerOf2Size");
            m_bIsSetup.boolValue = false;
        }
        public override void OnInspectorGUI()
        {
            Depthkit.StudioVFXTextureSource studioSrc = (Depthkit.StudioVFXTextureSource)target;
            serializedObject.Update();
            bool doRefresh = false;
            bool doUpdate = false;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(textureSizeReductionFactor);
            EditorGUILayout.PropertyField(usePowerOf2Size);
            if (EditorGUI.EndChangeCheck())
            {
                doRefresh = true;
                doUpdate = true;
            }

            m_showTextures = EditorGUILayout.Foldout(m_showTextures, "Data Textures Preview");
            if(m_showTextures)
            {
                if(studioSrc.positionDataTexture != null){
                    GUILayout.Label("Position Data:");
                    GUILayout.Label(studioSrc.positionDataTexture, GUILayout.Width(100), GUILayout.Height(100));
                }
                if(studioSrc.colorDataTexture != null){
                    GUILayout.Label("Color Data:");
                    GUILayout.Label(studioSrc.colorDataTexture, GUILayout.Width(100), GUILayout.Height(100));
                }
                if(studioSrc.normalDataTexture != null){
                    GUILayout.Label("Normal Data:");
                    GUILayout.Label(studioSrc.normalDataTexture, GUILayout.Width(100), GUILayout.Height(100));
                }
            }

            serializedObject.ApplyModifiedProperties();

            if(GUILayout.Button("Resize"))
            {
                doRefresh = true;
            }

            if(GUILayout.Button("Generate Data"))
            {
                doUpdate = true;
            }

            if(doRefresh)
            {
                studioSrc.Resize();
            }
            if(doUpdate)
            {
                studioSrc.Generate();
            }
        }
    }
}