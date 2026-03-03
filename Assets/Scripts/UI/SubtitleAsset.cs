using System;
using System.Collections.Generic;
using UnityEngine;

// The data structure for a single subtitle line
[Serializable]
public class SubtitleLine
{
    public int index;
    public float startTime; // In seconds
    public float endTime;   // In seconds
    [TextArea(2, 5)]
    public string text;
}

// The ScriptableObject that will hold the parsed file
public class SubtitleAsset : ScriptableObject
{
    public List<SubtitleLine> lines = new List<SubtitleLine>();
}