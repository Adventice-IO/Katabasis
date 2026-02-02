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
using System.Runtime.InteropServices;

namespace Depthkit
{
    [Serializable]
    public class SyncedStructuredBuffer<T>
    {
        public ComputeBuffer buffer;

        [SerializeField]
        protected T[] m_data = null;

        bool m_dirty = true;

        [SerializeField]
        string m_name;

        public SyncedStructuredBuffer(string name, int count, T[] defaultData = null)
        {
            m_name = name;
            if (defaultData != null)
            {
                m_data = defaultData;
            }
            else
            {
                m_data = new T[count];
            }
            MarkDirty();
        }

        public int Length { get { return m_data != null ? m_data.Length : 0; } }

        public void MarkDirty()
        {
            m_dirty = true;
        }

        public bool Sync()
        {
            if ((m_dirty || buffer == null || !buffer.IsValid()) && m_data != null && m_data.Length > 0)
            {
                if (Util.EnsureComputeBuffer(ComputeBufferType.Default, ref buffer, m_data.Length, Marshal.SizeOf(typeof(T)), m_data))
                {
                    if(m_name != string.Empty)
                    {
                        buffer.name = m_name;
                    }
                }
                m_dirty = false;
                return true;
            }
            return false;
        }

        public void Release()
        {
            if (buffer != null && buffer.IsValid())
            {
                buffer.Release();
            }
            buffer = null;
        }
    }
}