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

    public Vector3 offset = new Vector3(0, -.17f, .5f);
    [Range(0, 5)]
    public float smooth = 3f;

    UIDocument uiDocument;
    Label subtitleLabel;

    SubtitleLine curLine;

    DataManager dataManager;


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
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            //Debug.LogError("Cannot initialize subtitle document: UIDocument is null or rootVisualElement is null");
            return;
        }

        subtitleLabel = uiDocument.rootVisualElement.Q<Label>("subtitle");
        subtitleLabel.AddToClassList("hidden");
        //Debug.Log("Subtitle label: " + (subtitleLabel != null ? subtitleLabel.name : "null"));
    }

    private void Start()
    {
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if (subtitleLabel == null)
        {
            initDocument();
        }

        if (isPlaying != lastPlaying || (isPlaying && timeAtPlay == 0))
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


        Vector3 targetPosition = Camera.main.transform.TransformPoint(offset);
        Quaternion targetRotation = Quaternion.LookRotation(targetPosition - Camera.main.transform.position);

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smooth);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smooth);

        if (isPlaying && subs != null && timeAtPlay > 0)
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
        if (!dataManager.IsFolderReady(DataManager.DataFolder.Interviews))
        {
            dataManager.PreloadFolder(DataManager.DataFolder.Interviews, (success, path) =>
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

        timeAtPlay = 0;
        subs = LoadSubtitleAsset(relativeSubtitlePath);
        if (subs != null)
        {
            Debug.Log("Loading subtitles for " + relativeSubtitlePath + ", first line at " + (subs.lines != null && subs.lines.Count > 0 ? subs.lines[0].startTime.ToString("F2") : "no lines"));
        }

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
            Debug.LogWarning("Relative subtitle path is null or empty");
            return null;
        }

        string subtitlePath = dataManager.GetFilePath(DataManager.DataFolder.Interviews, relativeSubtitlePath);
        if (!File.Exists(subtitlePath))
        {
            Debug.LogWarning($"Subtitle file not found at path: {subtitlePath}");
            return null;
        }

        return SubtitleSrtParser.LoadFromFile(subtitlePath);
    }
}
