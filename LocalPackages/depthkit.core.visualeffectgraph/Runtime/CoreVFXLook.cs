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
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;
using System.Collections;
using System.IO;
using System;

namespace Depthkit
{
    [RequireComponent(typeof(VisualEffect))]
    [ExecuteInEditMode]
    [AddComponentMenu("Depthkit/Core/VFX/Depthkit Core Visual Effect Graph Look")]
    public class CoreVFXLook : Look
    {
        protected Depthkit.VFXTextureSource m_dataTextures;
        private VisualEffect m_visualEffect;   
        public override string GetLookName(){ return "Depthkit Core VFX Look"; }
        protected virtual void SetVisualEffectProperties(ref VisualEffect effect){}
        protected override bool UsesMaterial() { return false; }
        protected override Material GetMaterial() { return null; }
        protected override bool UsesMaterialPropertyBlock() { return false; }
        protected override MaterialPropertyBlock GetMaterialPropertyBlock() { return null; }

        protected override void SetDefaults()
        {
            m_visualEffect = GetComponent<VisualEffect>();
            if(m_visualEffect.visualEffectAsset == null)
            {
                m_visualEffect.visualEffectAsset = Resources.Load("VFX/Depthkit VFX Example Look", typeof(VisualEffectAsset)) as VisualEffectAsset;
            }
            base.SetDefaults();
        }
        protected override void SetDataSources()
        {
            if(meshSource == null)
            {
                meshSource = depthkitClip.GetDataSource<Depthkit.CoreMeshSource>();
            }
            if(m_dataTextures == null && meshSource != null)
            {
                m_dataTextures = meshSource.GetChild<Depthkit.CoreVFXTextureSource>();
            }
        }
        protected override bool ValidateDataSources()
        {
            return m_dataTextures != null && meshSource != null;
        }
        internal static class Properties
        {
            internal static readonly ExposedProperty
                PositionDataTexture = "PositionDataTexture",
                ColorDataTexture = "ColorDataTexture",
                NormalDataTexture = "NormalDataTexture", 
                DataWidth = "DataWidth",
                DataHeight = "DataHeight";
        }

        protected override void SetLookProperties()
        {
            if (m_dataTextures.positionDataTexture == null || m_dataTextures.normalDataTexture == null || m_dataTextures.colorDataTexture == null)
                return;

            SyncColliderToBounds();
            m_visualEffect.SetTexture(Properties.PositionDataTexture, m_dataTextures.positionDataTexture);
            m_visualEffect.SetTexture(Properties.NormalDataTexture, m_dataTextures.normalDataTexture);
            m_visualEffect.SetTexture(Properties.ColorDataTexture, m_dataTextures.colorDataTexture);
            m_visualEffect.SetInt(Properties.DataWidth, (int)m_dataTextures.dataTextureSize.x);
            m_visualEffect.SetInt(Properties.DataHeight, (int)m_dataTextures.dataTextureSize.y);
            SetVisualEffectProperties(ref m_visualEffect);
        }
    }
}
