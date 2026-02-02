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
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Depthkit
{
    [AddComponentMenu("Depthkit/Players/Depthkit Image Sequence Player")]
    [RequireComponent(typeof(Depthkit.Clip))]
    [DisallowMultipleComponent]
    public class StreamingImageSequencePlayer : Depthkit.ClipPlayer
    {

        [SerializeField, HideInInspector]
        
        protected Unity.StreamingImageSequence.StreamingImageSequenceRenderer m_sequence;

        [SerializeField, HideInInspector]

        protected PlayableDirector m_playableDirector;

        [SerializeField, HideInInspector]
        protected Vector2Int m_size;

        [SerializeField, HideInInspector]
        protected Depthkit.Clip m_clip;

        private RenderTexture m_targetTexture;

        private void EnsureTexture()
        {
            if(m_size.x == 0 || m_size.y == 0)
            {
                if(m_clip.metadata != null)
                {
                    m_size.x = m_clip.metadata.textureWidth;
                    m_size.y = m_clip.metadata.textureHeight;
                }
            } 

            if(m_size.x == 0 || m_size.y == 0) return;

            if(m_targetTexture == null || !m_targetTexture.IsCreated() || m_targetTexture.width != m_size.x || m_targetTexture.height != m_size.y)
            {
                m_targetTexture = new RenderTexture(m_size.x, m_size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                m_targetTexture.filterMode = FilterMode.Bilinear;
                m_targetTexture.enableRandomWrite = true;
                m_targetTexture.name = "Depthkit Streaming Image Sequence Target";
                m_targetTexture.Create();
                m_sequence.SetTargetTexture(m_targetTexture);
            }
        }

        public override void CreatePlayer()
        {
            m_clip = gameObject.GetComponent<Depthkit.Clip>();

            m_playableDirector = gameObject.GetComponent<PlayableDirector>();

            if(m_playableDirector == null)
            {
                m_playableDirector = gameObject.AddComponent<PlayableDirector>();
            }

            m_sequence = gameObject.GetComponent<Unity.StreamingImageSequence.StreamingImageSequenceRenderer>();

            if(m_sequence == null)
            {
                m_sequence = gameObject.AddComponent<Unity.StreamingImageSequence.StreamingImageSequenceRenderer>();
            }

            EnsureTexture();
        }

        public override bool IsPlayerCreated()
        {
            return m_playableDirector != null;
        }

        public override bool IsPlayerSetup()
        {
            return m_sequence != null && m_playableDirector.playableAsset != null;
        }

        /// <summary>
        /// Sets the video from a path. Assumed relative to data folder file path.</summary>
        public override void SetVideoPath(string path)
        {

        }

        /// <summary>
        /// Get the absolute path to the video.</summary>
        public override string GetVideoPath()
        {
            return "";
        }

        public override void StartVideoLoad()
        {
            if(!IsPlayerCreated() || !IsPlayerSetup()) return;
            StartCoroutine(Load());
        }

        public override IEnumerator Load()
        {
            events.OnClipLoadingStarted();
            videoLoaded = true;
            yield return null;
        }

        public override IEnumerator LoadAndPlay()
        {
            StartVideoLoad();
            while (!videoLoaded)
            {
                yield return null;
            }
            Play();
            yield return null;
        }

        public override void Play()
        {
            if(m_playableDirector.state == PlayState.Paused)
            {
                m_playableDirector.Resume();
            }
            else
            {
                m_playableDirector.Play();
            }
            events.OnClipPlaybackStarted();
        }
        public override void Pause()
        {
            m_playableDirector.Pause();
            events.OnClipPlaybackPaused();
        }
        public override void Stop()
        {
            m_playableDirector.Stop();
            events.OnClipPlaybackStopped();
        }

        public override int GetCurrentFrame()
        {
            return IsPlayerSetup() && IsPlayerCreated() ? Mathf.RoundToInt((float)(m_playableDirector.time * ((TimelineAsset)m_playableDirector.playableAsset).editorSettings.fps)) : -1;
        }
        public override double GetCurrentTime()
        {
            return m_playableDirector.time;
        }

        public override double GetDuration()
        {
            return m_playableDirector.duration;
        }

        public override Texture GetTexture()
        {
            EnsureTexture();
            return m_sequence.GetTargetTexture();
        }
        public override bool IsTextureFlipped ()
        {
            return false;
        }
        public override GammaCorrection GammaCorrectDepth()
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                return GammaCorrection.LinearToGammaSpace;
            }
            else
            {
                return GammaCorrection.None;
            }
        }
        public override GammaCorrection GammaCorrectColor()
        {
            return GammaCorrection.None;
        }
        public override bool IsPlaying()
        {
            return m_playableDirector.state == PlayState.Playing;
        }

        public override void RemoveComponents()
        {
            if(!Application.isPlaying)
            {
                DestroyImmediate(m_playableDirector, true);
                DestroyImmediate(m_sequence, true);
                m_targetTexture?.Release();
                DestroyImmediate(this, true);
            }
            else
            {
                Destroy(m_playableDirector);
                Destroy(m_sequence);
                m_targetTexture?.Release();
                Destroy(this);
            }
        }

        public override void OnMetadataUpdated(Depthkit.Metadata metadata)
        {
            m_size.x = metadata.textureWidth;
            m_size.y = metadata.textureHeight;
            EnsureTexture();
        }

        public override string GetPlayerTypeName()
        {
            return typeof(StreamingImageSequencePlayer).Name;
        }

        public new static string GetPlayerPrettyName()
        {
            return "Image Sequence Player";
        }

        public PlayableDirector GetPlayerBackend()
        {
            return m_playableDirector;
        }

        public override void Seek(float toTime)
        {
            m_playableDirector.time = toTime;
        }

        public override uint GetVideoWidth()
        {
            return (uint)m_size.x ;
        }

        public override uint GetVideoHeight()
        {
            return (uint)m_size.y;
        } 

        public override bool SupportsPosterFrame()
        {
            return false;
        }
    }
}

