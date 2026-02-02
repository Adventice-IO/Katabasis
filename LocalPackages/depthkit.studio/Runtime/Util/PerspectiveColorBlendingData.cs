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
    public struct PerspectiveColorBlending
    {
#pragma warning disable CS0414
        public static PerspectiveColorBlending[] Create(int count)
        {
            PerspectiveColorBlending[] data = new PerspectiveColorBlending[count];
            for (int i = 0; i < count; ++i)
            {
                data[i].enabled = 1;
                data[i].viewWeightPowerContribution = 1.0f;
            }
            return data;
        }

        public int enabled;
        public float viewWeightPowerContribution;
    };

#pragma warning restore CS0414

    [Serializable]
    public class PerspectiveColorBlendingData : SyncedStructuredBuffer<PerspectiveColorBlending>
    {
        public PerspectiveColorBlendingData(string name, int count) : base(name, count, PerspectiveColorBlending.Create(count))
        { }

        public float GetViewDependentColorBlendContribution(int perspective)
        {
            return m_data[perspective].viewWeightPowerContribution;
        }

        public void SetViewDependentColorBlendContribution(int perspective, float contribution)
        {
            contribution = Mathf.Clamp01(contribution);
            if (!Mathf.Approximately(contribution, m_data[perspective].viewWeightPowerContribution))
            {
                m_data[perspective].viewWeightPowerContribution = contribution;
                MarkDirty();
            }
        }

        public bool GetPerspectiveEnabled(int perspective)
        {
            return m_data[perspective].enabled == 1;
        }

        public void SetPerspectiveEnabled(int perspective, bool enabled)
        {
            if ((m_data[perspective].enabled == 1) != enabled)
            {
                m_data[perspective].enabled = enabled ? 1 : 0;
                MarkDirty();
            }
        }
    };
}