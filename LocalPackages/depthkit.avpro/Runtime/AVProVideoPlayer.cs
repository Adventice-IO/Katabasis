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
using System.Reflection;
using RenderHeads.Media.AVProVideo;

#if !DK_AVPROv1

namespace Depthkit
{
    /// <summary>
    /// Implementation of the Depthkit player with an AVProVideo 2.x backend </summary>
    [AddComponentMenu("Depthkit/Players/Depthkit Video Player (AVPro)")]
    public class AVProVideoPlayer : Depthkit.ClipPlayer
    {
        [SerializeField, HideInInspector]
        /// <summary>
        /// Reference to the AVProVideo Component </summary>
        protected MediaPlayer m_mediaPlayer;

        [HideInInspector]
        // Note: on AVProVideo 2.9.1 when building to Quest, and possibly all Android devices,
        // There is a crash when exposing MediaPlayer.TextureProvider.GetTexture() directly.
        // To work around this, we use a RenderTexture and Graphics.Blit to copy the video
        // texture, and then return the copy. Whenever AVPro fixes this issue,
        // we can remove this work around.
        public bool disableAndroidTextureCopyWorkAround = false;

        protected RenderTexture m_rt = null;

        string m_path;
        MediaPathType m_pathType;

        public override void CreatePlayer()
        {
            m_mediaPlayer = gameObject.GetComponent<MediaPlayer>();
            if (m_mediaPlayer == null)
            {
                // no media component already added to this component, try adding a MediaPlayer component
                try
                {
                    m_mediaPlayer = gameObject.AddComponent<MediaPlayer>();
                }
                catch (Exception e)
                {
                    Debug.LogError("AVProVideo not found in project: " + e.ToString());
                }
            }
        }

        public override bool IsPlayerCreated()
        {
            return m_mediaPlayer != null;
        }

        public override bool IsPlayerSetup()
        {
            if (!IsPlayerCreated())
            {
                return false;
            }
            return m_mediaPlayer.MediaPath != null && (m_mediaPlayer.MediaSource == MediaSource.Path ? m_mediaPlayer.MediaPath.Path != "" : m_mediaPlayer.MediaReference != null);
        }

        /// <summary>
        /// Sets the video from a path. Assumed relative to data folder path.</summary>
        public override void SetVideoPath(string path)
        {
            if (!IsPlayerCreated())
            {
                return;
            }

            path = path.Replace("\\", "/");
            if (path.StartsWith(Application.dataPath))
            {
                path = "Assets" + path.Substring(Application.dataPath.Length);
            }

            m_path = path;
            m_pathType = MediaPathType.RelativeToProjectFolder;
        }

        /// <summary>
        /// Get the absolute path to the video.</summary>
        public override string GetVideoPath()
        {
            if (!IsPlayerSetup())
            {
                return "";
            }
            return m_mediaPlayer.MediaPath.GetResolvedFullPath();
        }

        public override IEnumerator Load()
        {
            //start the loading operation
            m_mediaPlayer.OpenMedia(new MediaPath(m_path, m_pathType), false);
            events.OnClipLoadingStarted();

            //while the video is loading you can't play it
            while (!m_mediaPlayer.Control.CanPlay())
            {
                videoLoaded = false;
                yield return null;
            }
            videoLoaded = true;
            events.OnClipLoadingFinished();
            yield return null;
        }

        public override void StartVideoLoad()
        {
            StartCoroutine(Load());
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
            if (videoLoaded || m_mediaPlayer.MediaOpened)
            {
                videoLoaded = true;
                m_mediaPlayer.Control.Play();
                events.OnClipPlaybackStarted();
            }
        }
        public override void Pause()
        {
            m_mediaPlayer.Control.Pause();
            events.OnClipPlaybackPaused();
        }
        public override void Stop()
        {
            if (m_mediaPlayer != null && m_mediaPlayer.Control != null)
                m_mediaPlayer.Control.Stop();
            events.OnClipPlaybackStopped();
        }

        public override double GetCurrentTime()
        {
            return m_mediaPlayer.Control.GetCurrentTime();
        }

        public override int GetCurrentFrame()
        {
            if (m_mediaPlayer != null && m_mediaPlayer.TextureProducer != null)
            {
                return m_mediaPlayer.TextureProducer.GetTextureFrameCount();
            }
            return -1;
        }

        public override double GetDuration()
        {
            return m_mediaPlayer.Info.GetDuration();
        }

        public override Texture GetTexture()
        {
            if (m_mediaPlayer != null && m_mediaPlayer.MediaOpened && m_mediaPlayer.TextureProducer != null)
            {
                Texture2D tex = m_mediaPlayer.TextureProducer.GetTexture() as Texture2D;
                if (tex != null)
                {
                    // Avoid crash on android when directly returning the TextureProducer.GetTexture()
                    if (Application.platform == RuntimePlatform.Android  && !disableAndroidTextureCopyWorkAround)
                    {
                        if (m_rt == null ||
                        m_rt.width != tex.width ||
                        m_rt.height != tex.height)
                        {
                            if (m_rt != null)
                            {
                                m_rt.Release();
                                m_rt = null;
                            }
                            m_rt = new RenderTexture(tex.width, tex.height, 0);
                            m_rt.Create();
                        }

                        Graphics.Blit(tex, m_rt);

                        return m_rt;
                    }
                    else
                    {
                        return tex;
                    }
                }
            }

            return null;
        }
        public override bool IsTextureFlipped()
        {
            if (m_mediaPlayer != null && m_mediaPlayer.TextureProducer != null)
            {
                return m_mediaPlayer.TextureProducer.RequiresVerticalFlip();
            }
            return false;
        }

        public override GammaCorrection GammaCorrectDepth()
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                if (m_mediaPlayer.Info != null && !m_mediaPlayer.Info.PlayerSupportsLinearColorSpace())
                {
                    return GammaCorrection.None;
                }
                else
                {
                    return GammaCorrection.LinearToGammaSpace;
                }
            }
            else // ColorSpace.Gamma
            {
                return GammaCorrection.None;
            }
        }

        public override GammaCorrection GammaCorrectColor()
        {

            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                if (m_mediaPlayer.Info != null && !m_mediaPlayer.Info.PlayerSupportsLinearColorSpace())
                {
                    return GammaCorrection.GammaToLinearSpace;
                }
                else
                {
                    return GammaCorrection.None;
                }
            }
            else // ColorSpace.Gamma
            {
                return GammaCorrection.None;
            }
        }

        public override bool IsPlaying()
        {
            return (!IsPlayerSetup() || !IsPlayerCreated() || m_mediaPlayer.Control == null) ? false : m_mediaPlayer.Control.IsPlaying();
        }

        public override void RemoveComponents()
        {
            if (!Application.isPlaying)
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

        public override void OnMetadataUpdated(Depthkit.Metadata metadata) { /* do nothing */}

        public override string GetPlayerTypeName()
        {
            return typeof(Depthkit.AVProVideoPlayer).Name;
        }

        public new static string GetPlayerPrettyName()
        {
            return "Video Player (AVPro)";
        }

        public RenderHeads.Media.AVProVideo.MediaPlayer GetPlayerBackend()
        {
            return m_mediaPlayer;
        }

        public override void Seek(float toTimeSeconds)
        {
            if (m_mediaPlayer == null)
            {
                return;
            }

            if (m_mediaPlayer.Control == null)
            {
                var Init = m_mediaPlayer.GetType().GetMethod("Initialise", BindingFlags.NonPublic | BindingFlags.Instance);
                Init?.Invoke(m_mediaPlayer, null);
            }

            var control = m_mediaPlayer.Control;
            if (control == null)
            {
                return;
            }

            if (control.IsPlaying())
            {
                control.Pause(); // Pause since we'll just be seeking.
            }

            float currentTime = (float)control.GetCurrentTime();
            float directorTime = toTimeSeconds;

            if (!Mathf.Approximately(currentTime, directorTime))
            {
                var preSeekFrameCount = m_mediaPlayer.TextureProducer.GetTextureFrameCount();

                float preSeekTime = (float)control.GetCurrentTime();

                control.Seek(directorTime);

                float postSeekTime = (float)control.GetCurrentTime();

                if (!Mathf.Approximately(preSeekTime, postSeekTime))
                {
                    control.WaitForNextFrame(GetDummyCamera(), preSeekFrameCount);
                }
            }

            m_mediaPlayer.Player.Update();
        }

        static Camera _dummyCamera;
        private static Camera GetDummyCamera()
        {
            if (_dummyCamera == null)
            {
                const string goName = "Video Dummy Camera";
                GameObject go = GameObject.Find(goName);
                if (go == null)
                {
                    go = new GameObject(goName);
                    go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    go.SetActive(false);
                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(go);
                    }

                    _dummyCamera = go.AddComponent<Camera>();
                    _dummyCamera.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
                    _dummyCamera.cullingMask = 0;
                    _dummyCamera.clearFlags = CameraClearFlags.Nothing;
                    _dummyCamera.enabled = false;
                }
                else
                {
                    _dummyCamera = go.GetComponent<Camera>();
                }
            }
            return _dummyCamera;
        }
        public override uint GetVideoWidth()
        {
            return m_mediaPlayer != null && m_mediaPlayer.Info != null ? (uint)m_mediaPlayer.Info.GetVideoWidth() : 0;
        }
        public override uint GetVideoHeight()
        {
            return m_mediaPlayer != null && m_mediaPlayer.Info != null ? (uint)m_mediaPlayer.Info.GetVideoHeight() : 0;
        }

        public override bool SupportsPosterFrame()
        {
            return true;
        }

        void OnDisable()
        {
            if (m_rt != null)
            {
                m_rt.Release();
                m_rt = null;
            }
        }
    }
}

#endif