using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "srt")]
public class SubtitleImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        //1.Create a new instance of our ScriptableObject
        var subtitleAsset = ScriptableObject.CreateInstance<SubtitleAsset>();

        // 2. Read the raw text from the file
        string text = File.ReadAllText(ctx.assetPath);

        // 3. Parse the SRT text
        ParseSrt(text, subtitleAsset);

        // 4. Register the ScriptableObject as the main imported asset
        ctx.AddObjectToAsset("MainAsset", subtitleAsset);
        ctx.SetMainObject(subtitleAsset);
    }

    private void ParseSrt(string text, SubtitleAsset asset)
    {
        // SRT files are separated by double newlines
        string[] blocks = text.Split(new string[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            // Split each block into individual lines
            string[] lines = block.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (lines.Length >= 3)
            {
                // Line 1: Subtitle Index
                int.TryParse(lines[0].Trim(), out int index);

                // Line 2: Timestamps (e.g., "00:00:01,000 --> 00:00:04,000")
                string[] times = lines[1].Split(new string[] { " --> " }, StringSplitOptions.None);
                if (times.Length == 2)
                {
                    float start = ParseSrtTime(times[0]);
                    float end = ParseSrtTime(times[1]);

                    // Line 3+: The actual subtitle text (could be multiple lines)
                    string subText = string.Join("\n", lines, 2, lines.Length - 2).Trim();

                    asset.lines.Add(new SubtitleLine
                    {
                        index = index,
                        startTime = start,
                        endTime = end,
                        text = subText
                    });
                }
            }
        }
    }

    private float ParseSrtTime(string timeStr)
    {
        // SRT uses commas for milliseconds, but C# TimeSpan prefers periods
        timeStr = timeStr.Trim().Replace(',', '.');

        if (TimeSpan.TryParse(timeStr, out TimeSpan timeSpan))
        {
            return (float)timeSpan.TotalSeconds;
        }

        Debug.LogWarning($"Failed to parse time: {timeStr}");
        return 0f;
    }
}