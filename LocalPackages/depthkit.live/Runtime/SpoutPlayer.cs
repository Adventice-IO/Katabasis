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
using System.Collections;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Depthkit")]

namespace Depthkit.Live
{
    /// <summary>
    /// Implementation of the DepthKit player with the Spout backend.
    /// </summary>
    [AddComponentMenu("Depthkit/Players/Depthkit Spout Player")]
    public class SpoutPlayer : Depthkit.ClipPlayer
    {
        //reference to the MovieTexture passed in through Clip
        [SerializeField, HideInInspector]
        
        protected Klak.Spout.SpoutReceiver m_mediaPlayer;
        private int m_frame = 0;

        public override void CreatePlayer()
        {
            m_mediaPlayer = gameObject.GetComponent<Klak.Spout.SpoutReceiver>();

            if (m_mediaPlayer == null)
            {
                m_mediaPlayer = gameObject.AddComponent<Klak.Spout.SpoutReceiver>();
                m_mediaPlayer.sourceName = "Depthkit";
            }
        }

        public override bool IsPlayerCreated()
        {
            return m_mediaPlayer != null;
        }

        public override bool IsPlayerSetup()
        {
            return true;
        }

        /// <summary>
        /// Sets the video from a path. Assumed relative to data foldder file path.</summary>
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
            
            StartCoroutine(Load());
        }

        public override IEnumerator Load()
        {
            events.OnClipLoadingStarted();
            videoLoaded = true;
            yield return null;
        }

        public void OnVideoLoadingComplete(Klak.Spout.SpoutReceiver player)
        {
            videoLoaded = true;
            events.OnClipLoadingFinished();
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
            //automatically plays for now
            events.OnClipPlaybackStarted();
        }
        public override void Pause()
        {
            // _mediaPlayer.Pause();
            events.OnClipPlaybackPaused();
        }
        public override void Stop()
        {
            // _mediaPlayer.Stop();
            events.OnClipPlaybackStopped();
        }

        public override int GetCurrentFrame()
        {
            return m_frame++;
        }
        public override double GetCurrentTime()
        {
            return 0;
        }

        public override double GetDuration()
        {
            return 0;
        }

        public override Texture GetTexture()
        {
            return m_mediaPlayer.receivedTexture;
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
            return true;
        }

        public override void RemoveComponents()
        {
            if(!Application.isPlaying)
            {
                DestroyImmediate(m_mediaPlayer, true);
                DestroyImmediate(this, true);
            }
            else
            {
                Destroy(m_mediaPlayer);
                Destroy(this);
            }
        }

        public override void OnMetadataUpdated(Depthkit.Metadata metadata)
        {
        }

        public override string GetPlayerTypeName()
        {
            return typeof(SpoutPlayer).Name;
        }

        public new static string GetPlayerPrettyName()
        {
            return "Livestream Player (Spout)";
        }

        public Klak.Spout.SpoutReceiver GetPlayerBackend()
        {
            return m_mediaPlayer;
        }

        public override void Seek(float toTime)
        {
            //do nothing
        }

        public override uint GetVideoWidth()
        {
            return m_mediaPlayer != null && m_mediaPlayer.targetTexture != null ? (uint)m_mediaPlayer.targetTexture.width : 0;
        }

        public override uint GetVideoHeight()
        {
            return m_mediaPlayer != null && m_mediaPlayer.targetTexture != null ? (uint)m_mediaPlayer.targetTexture.height : 0;
        }

        public override bool SupportsPosterFrame()
        {
            return false;
        }
    }
}

