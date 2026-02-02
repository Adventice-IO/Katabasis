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

namespace Depthkit
{
     public delegate void DataSourceEventHandler();

    /// <summary>
    /// Class that contains events a given player could potentially emit for listening. </summary>
    [System.Serializable]
    public class DataSourceEvents 
    {
        private event DataSourceEventHandler m_dataGenerated;
        public event DataSourceEventHandler dataGenerated
        {
            add
            {
                if (m_dataGenerated != null)
                {
                    foreach(DataSourceEventHandler existingHandler in m_dataGenerated.GetInvocationList())
                    {
                        if (existingHandler == value)
                        {
                            return;
                        }
                    }
                }
                m_dataGenerated += value;
            }
            remove
            {
                if (m_dataGenerated != null)
                {
                    foreach(DataSourceEventHandler existingHandler in m_dataGenerated.GetInvocationList())
                    {
                        if (existingHandler == value)
                        {
                            m_dataGenerated -= value;
                        }
                    }
                }
            }
        }
        private event DataSourceEventHandler m_dataResized;
        public event DataSourceEventHandler dataResized
        {
            add
            {
                if (m_dataResized != null)
                {
                    foreach(DataSourceEventHandler existingHandler in m_dataResized.GetInvocationList())
                    {
                        if (existingHandler == value)
                        {
                            return;
                        }
                    }
                }
                m_dataResized += value;
            }
            remove
            {
                if (m_dataResized != null)
                {
                    foreach(DataSourceEventHandler existingHandler in m_dataResized.GetInvocationList())
                    {
                        if (existingHandler == value)
                        {
                            m_dataResized -= value;
                        }
                    }
                }
            }
        }

        public virtual void OnDataGenerated()
        {   
            if(m_dataGenerated != null) { m_dataGenerated(); }
        }
        public virtual void OnDataResized()
        {   
            if(m_dataResized != null) { m_dataResized(); }
        }
    }

}