using System;
using System.Collections.Generic;
using System.IO;
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
            //Debug.LogError("No UIDocument found on Subtitles component");
            return;
        }
        //uiDocument.enabled = true;
        initDocument();
    }

    void initDocument()
    {
        if(uiDocument == null || uiDocument.rootVisualElement == null)
        {
            //Debug.LogError("Cannot initialize subtitle document: UIDocument is null or rootVisualElement is null");
            return;
        }

        subtitleLabel = uiDocument.rootVisualElement.Q<Label>("subtitle");
        subtitleLabel.AddToClassList("hidden");
        //Debug.Log("Subtitle label: " + (subtitleLabel != null ? subtitleLabel.name : "null"));
    }

    // Update is called once per frame
    void Update()
    {
        if(subtitleLabel == null)
        {
            initDocument();
        }

        if (isPlaying != lastPlaying)
        {
            //Debug.Log("Subtitle playback started at " + Time.time);
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
                //Debug.Log($"Subtitle changed at {timeSincePlay:F2}s: {(subtitle != null ? subtitle.text : "null")}");
                curLine = subtitle;

                if (curLine != null)
                {
                    //Debug.Log($"Current subtitle: {curLine.text}");
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

    public void play(string relativeSubtitlePath)
    {
        if (!DataManager.IsFolderReady(DataManager.DataFolder.Interviews))
        {
            DataManager.PreloadFolder(DataManager.DataFolder.Interviews, (success, path) =>
            {
                if (!success)
                {
                    subs = null;
                    isPlaying = false;
                    return;
                }

                subs = LoadSubtitleAsset(relativeSubtitlePath);
                isPlaying = subs != null;
            });
            return;
        }

        subs = LoadSubtitleAsset(relativeSubtitlePath);
        isPlaying = subs != null;
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

    SubtitleAsset LoadSubtitleAsset(string relativeSubtitlePath)
    {
        if (string.IsNullOrWhiteSpace(relativeSubtitlePath))
        {
            return null;
        }

        string subtitlePath = DataManager.GetFilePath(DataManager.DataFolder.Interviews, relativeSubtitlePath);
        if (!File.Exists(subtitlePath))
        {
            return null;
        }

        SubtitleAsset asset = ScriptableObject.CreateInstance<SubtitleAsset>();
        ParseSrt(File.ReadAllText(subtitlePath), asset);
        return asset;
    }

    void ParseSrt(string text, SubtitleAsset asset)
    {
        if (asset == null)
        {
            return;
        }

        string[] blocks = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string block in blocks)
        {
            string[] lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length < 3)
            {
                continue;
            }

            int.TryParse(lines[0].Trim(), out int index);
            string[] times = lines[1].Split(new[] { " --> " }, StringSplitOptions.None);
            if (times.Length != 2)
            {
                continue;
            }

            asset.lines.Add(new SubtitleLine
            {
                index = index,
                startTime = ParseSrtTime(times[0]),
                endTime = ParseSrtTime(times[1]),
                text = string.Join("\n", lines, 2, lines.Length - 2).Trim()
            });
        }
    }

    float ParseSrtTime(string timeStr)
    {
        timeStr = timeStr.Trim().Replace(',', '.');
        return TimeSpan.TryParse(timeStr, out TimeSpan timeSpan) ? (float)timeSpan.TotalSeconds : 0f;
    }
}
