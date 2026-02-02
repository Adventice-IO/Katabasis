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

namespace Depthkit
{
    [Serializable]
    public struct PerspectiveGeometry
    {
#pragma warning disable CS0414
        public static PerspectiveGeometry[] Create(int count)
        {
            PerspectiveGeometry[] data = new PerspectiveGeometry[count];
            for (int i = 0; i < count; ++i)
            {
                data[i].enabled = 1;
                data[i].weightUnknown = 0.005f;
                data[i].viewDependentUnseenAmount = 1.0f;
                data[i].viewDependentInFrontAmount = 1.0f;
                data[i].viewDependentWeight = 1.0f;
                data[i].overrideWeightUnknown = 0;
                data[i].pad2 = 0.0f;
                data[i].pad1 = 0.0f;
            }
            return data;
        }

        public int enabled;
        public int overrideWeightUnknown;
        public float weightUnknown;
        public float viewDependentUnseenAmount;
        public float viewDependentInFrontAmount;
        public float viewDependentWeight;
        float pad2;
        float pad1;
    };
#pragma warning restore CS0414

    [Serializable]
    public class PerspectiveGeometryData : SyncedStructuredBuffer<PerspectiveGeometry>
    {
        [SerializeField]
        private bool[] m_geometryMatchesColorWeights;

        [SerializeField]
        private float[] m_viewDependentContributions;

        public PerspectiveGeometryData(string name, int count) : base(name, count, PerspectiveGeometry.Create(count))
        {
            m_geometryMatchesColorWeights = new bool[count];
            m_viewDependentContributions = new float[count];
            for (int i = 0; i < count; ++i)
            {
                m_geometryMatchesColorWeights[i] = false;
                m_viewDependentContributions[i] = 1.0f;
            }
        }

        public bool EnableGeometry(int perspective)
        {
            return m_data[perspective].enabled == 1;
        }

        public void EnableGeometry(int perspective, bool enable)
        {
            if ((m_data[perspective].enabled == 1) != enable)
            {
                m_data[perspective].enabled = enable ? 1 : 0;
                MarkDirty();
            }
        }

        public bool GetOverrideWeightUnknown(int perspective)
        {
            return m_data[perspective].overrideWeightUnknown == 1;
        }

        public void SetOverrideWeightUnknown(int perspective, bool enable)
        {
            if ((m_data[perspective].overrideWeightUnknown == 1) != enable)
            {
                m_data[perspective].overrideWeightUnknown = enable ? 1 : 0;
                MarkDirty();
            }
        }

        public float GetWeightUnknown(int perspective)
        {
            return m_data[perspective].weightUnknown;
        }

        public void SetWeightUnknown(int perspective, float weight)
        {
            weight = Mathf.Clamp(weight, 0.0001f, 0.05f);
            if (!Mathf.Approximately(weight, m_data[perspective].weightUnknown))
            {
                m_data[perspective].weightUnknown = weight;
                MarkDirty();
            }
        }

        public bool MatchViewDependentColorWeight(int perspective)
        {
            return m_geometryMatchesColorWeights[perspective];
        }

        public void MatchViewDependentColorWeight(int perspective, bool match)
        {
            m_geometryMatchesColorWeights[perspective] = match;
        }

        public float GetViewDependentContribution(int perspective)
        {
            return m_viewDependentContributions[perspective];
        }

        public void SetViewDependentContribution(int perspective, float contribution)
        {
            contribution = Mathf.Clamp01(contribution);
            m_viewDependentContributions[perspective] = contribution;
        }

        public float GetViewDependentWeight(int perspective)
        {
            return m_data[perspective].viewDependentWeight;
        }

        public void SetViewDependentWeight(int perspective, float contribution)
        {
            contribution = Mathf.Clamp01(contribution);
            if(!Mathf.Approximately(contribution, m_data[perspective].viewDependentWeight))
            {
                m_data[perspective].viewDependentWeight = contribution;
                MarkDirty();
            }
        }

        public float GetViewDependentInFrontAmount(int perspective)
        {
            return m_data[perspective].viewDependentInFrontAmount;
        }

        public void SetViewDependentInFrontAmount(int perspective, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(amount, m_data[perspective].viewDependentInFrontAmount))
            {
                m_data[perspective].viewDependentInFrontAmount = amount;
                MarkDirty();
            }
        }

        public float GetViewDependentUnseenAmount(int perspective)
        {
            return m_data[perspective].viewDependentUnseenAmount;
        }

        public void SetViewDependentUnseenAmount(int perspective, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(amount, m_data[perspective].viewDependentUnseenAmount))
            {
                m_data[perspective].viewDependentUnseenAmount = amount;
                MarkDirty();
            }
        }

    };
}