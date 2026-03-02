using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

[ExecuteInEditMode]
public class Subtitles : MonoBehaviour
{
    public SubtitleAsset subs;

    float timeAtPlay;

    public bool isPlaying = false;
    bool lastPlaying = false;


    UIDocument uiDocument;
    Label subtitleLabel;

    SubtitleLine curLine;

    void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("No UIDocument found on Subtitles component");
            return;
        }
        //uiDocument.enabled = true;
        Debug.Log("UIDocument found: " + uiDocument.name + " > " + uiDocument.rootVisualElement);
        subtitleLabel = uiDocument.rootVisualElement.Q<Label>("subtitle");
        subtitleLabel.AddToClassList("hidden");
        Debug.Log("Subtitle label: " + (subtitleLabel != null ? subtitleLabel.name : "null"));
    }

    // Update is called once per frame
    void Update()
    {
        if (isPlaying != lastPlaying)
        {
            Debug.Log("Subtitle playback started at " + Time.time);
            subtitleLabel.AddToClassList("hidden");
            if (isPlaying)
            {
                timeAtPlay = Time.time;
            }   
            lastPlaying = isPlaying;
            //uiDocument.enabled = true;
        }

        if (subtitleLabel == null)
        {
            //Debug.LogError("No subtitle label found in UIDocument");
            return;
        }

        if (isPlaying && subs != null)
        {
            float timeSincePlay = Time.time - timeAtPlay;
            SubtitleLine subtitle = getSubtitleAt(timeSincePlay, out bool isFinished);
            if (curLine != subtitle)
            {
                Debug.Log($"Subtitle changed at {timeSincePlay:F2}s: {(subtitle != null ? subtitle.text : "null")}");
                curLine = subtitle;

                if (curLine != null)
                {
                    Debug.Log($"Current subtitle: {curLine.text}");
                    subtitleLabel.RemoveFromClassList("hidden");
                    subtitleLabel.text = subtitle.text;
                }
                else
                {
                    if (isFinished)
                    {
                        Debug.Log("Subtitle playback finished at " + Time.time);
                        isPlaying = false;
                    }
                    subtitleLabel.AddToClassList("hidden");
                }
            }

        }
        else
        {
            //uiDocument.enabled = false;
        }
    }

    public void play(SubtitleAsset file)
    {
        subs = file;
        isPlaying = true;
        //uiDocument.enabled = true;
    }

    public void stop()
    {
        isPlaying = false;
        //uiDocument.enabled = false;
    }


    SubtitleLine getSubtitleAt(float time, out bool isFinished)
    {
        if (subs == null || subs.lines == null)
        {
            isFinished = true;
            return null;
        }

        bool isAfterLastLine = time > subs.lines[subs.lines.Count - 1].endTime;
        isFinished = isAfterLastLine;

        foreach (var subtitle in subs.lines)
        {
            if (subtitle.startTime <= time && subtitle.endTime >= time)
            {
                return subtitle;
            }
        }

        return null;
    }
}
