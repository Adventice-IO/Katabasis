using System;
using System.IO;
using UnityEngine;

public static class SubtitleSrtParser
{
    public static SubtitleAsset LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return Parse(File.ReadAllText(filePath));
    }

    public static SubtitleAsset Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        SubtitleAsset asset = ScriptableObject.CreateInstance<SubtitleAsset>();
        ParseInto(text, asset);
        return asset;
    }

    public static void ParseInto(string text, SubtitleAsset asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        asset.lines.Clear();

        string[] blocks = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < blocks.Length; i++)
        {
            string[] lines = blocks[i].Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
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
                startTime = ParseTime(times[0]),
                endTime = ParseTime(times[1]),
                text = string.Join("\n", lines, 2, lines.Length - 2).Trim()
            });
        }
    }

    public static float ParseTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr))
        {
            return 0f;
        }

        timeStr = timeStr.Trim().Replace(',', '.');
        return TimeSpan.TryParse(timeStr, out TimeSpan timeSpan) ? (float)timeSpan.TotalSeconds : 0f;
    }
}
