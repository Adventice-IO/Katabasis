using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor window for copying positions from source objects
/// and pasting them onto selected objects in bulk.
/// Usage: Window > Tools > Position Copy & Paste
/// </summary>
public class PositionCopyPaste : EditorWindow
{
    // ── State ────────────────────────────────────────────────────────────────
    private List<Vector3> copiedPositions = new List<Vector3>();
    private List<string>  sourceNames     = new List<string>();

    private bool  useLocalSpace   = false;
    private bool  pasteX          = true;
    private bool  pasteY          = true;
    private bool  pasteZ          = true;
    private bool  showCopiedList  = true;

    private Vector2 scrollPos;

    // ── Menu entry ───────────────────────────────────────────────────────────
    [MenuItem("Window/Tools/Position Copy & Paste")]
    public static void ShowWindow()
    {
        var window = GetWindow<PositionCopyPaste>("Position Copy & Paste");
        window.minSize = new Vector2(320, 420);
    }

    // ── GUI ──────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUILayout.Space(8);
        DrawTitle();
        GUILayout.Space(6);

        DrawOptions();
        GUILayout.Space(8);

        DrawCopySection();
        GUILayout.Space(8);

        DrawPasteSection();
        GUILayout.Space(8);

        DrawCopiedList();
    }

    // ── Sections ─────────────────────────────────────────────────────────────
    private void DrawTitle()
    {
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label("Position Copy & Paste", titleStyle);
        DrawSeparator();
    }

    private void DrawOptions()
    {
        GUILayout.Label("Options", EditorStyles.boldLabel);

        using (new GUILayout.HorizontalScope())
        {
            useLocalSpace = EditorGUILayout.Toggle("Local Space", useLocalSpace);
            GUILayout.Label(useLocalSpace ? "(local position)" : "(world position)",
                            EditorStyles.miniLabel);
        }

        GUILayout.Space(4);
        GUILayout.Label("Axes to paste:", EditorStyles.miniLabel);
        using (new GUILayout.HorizontalScope())
        {
            pasteX = GUILayout.Toggle(pasteX, " X", GUILayout.Width(40));
            pasteY = GUILayout.Toggle(pasteY, " Y", GUILayout.Width(40));
            pasteZ = GUILayout.Toggle(pasteZ, " Z", GUILayout.Width(40));
        }
    }

    private void DrawCopySection()
    {
        DrawSeparator();
        GUILayout.Label("① Copy", EditorStyles.boldLabel);

        int selCount = Selection.gameObjects.Length;
        EditorGUILayout.HelpBox(
            selCount == 0
                ? "Select one or more objects to copy their positions."
                : $"{selCount} object(s) selected.",
            selCount == 0 ? MessageType.Info : MessageType.None);

        using (new EditorGUI.DisabledGroupScope(selCount == 0))
        {
            if (GUILayout.Button($"Copy positions ({selCount} objects)", GUILayout.Height(30)))
                CopyPositions();
        }

        if (copiedPositions.Count > 0)
        {
            if (GUILayout.Button("Clear copied positions", EditorStyles.miniButton))
            {
                copiedPositions.Clear();
                sourceNames.Clear();
            }
        }
    }

    private void DrawPasteSection()
    {
        DrawSeparator();
        GUILayout.Label("② Paste", EditorStyles.boldLabel);

        if (copiedPositions.Count == 0)
        {
            EditorGUILayout.HelpBox("No positions copied yet.", MessageType.Warning);
            return;
        }

        int selCount = Selection.gameObjects.Length;

        if (selCount == 0)
        {
            EditorGUILayout.HelpBox("Select the objects that should receive the positions.", MessageType.Info);
            return;
        }

        // Explain matching strategy
        string strategy = selCount == 1 && copiedPositions.Count > 1
            ? $"1 target → all {copiedPositions.Count} copied positions will apply to it (last wins).\nTip: select {copiedPositions.Count} targets for a 1-to-1 match."
            : selCount == copiedPositions.Count
                ? $"✓ 1-to-1 match: {selCount} targets / {copiedPositions.Count} positions."
                : $"{selCount} targets / {copiedPositions.Count} positions → positions cycle if needed.";

        EditorGUILayout.HelpBox(strategy, MessageType.None);
        GUILayout.Space(4);

        using (new EditorGUI.DisabledGroupScope(selCount == 0))
        {
            if (GUILayout.Button($"Paste to {selCount} selected object(s)", GUILayout.Height(30)))
                PastePositions();
        }
    }

    private void DrawCopiedList()
    {
        if (copiedPositions.Count == 0) return;

        DrawSeparator();
        showCopiedList = EditorGUILayout.Foldout(showCopiedList,
            $"Copied positions ({copiedPositions.Count})", true);

        if (!showCopiedList) return;

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(160));
        for (int i = 0; i < copiedPositions.Count; i++)
        {
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"{i + 1}. {sourceNames[i]}", EditorStyles.miniLabel,
                    GUILayout.Width(120));
                GUILayout.Label(copiedPositions[i].ToString("F2"), EditorStyles.miniLabel);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    // ── Logic ─────────────────────────────────────────────────────────────────
    private void CopyPositions()
    {
        copiedPositions.Clear();
        sourceNames.Clear();

        // Preserve scene hierarchy order
        var sorted = new List<GameObject>(Selection.gameObjects);
        sorted.Sort((a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        foreach (var go in sorted)
        {
            copiedPositions.Add(useLocalSpace
                ? go.transform.localPosition
                : go.transform.position);
            sourceNames.Add(go.name);
        }

        Debug.Log($"[PositionCopyPaste] Copied {copiedPositions.Count} position(s) " +
                  $"({(useLocalSpace ? "local" : "world")} space).");
    }

    private void PastePositions()
    {
        if (copiedPositions.Count == 0) return;

        var targets = new List<GameObject>(Selection.gameObjects);
        targets.Sort((a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        Undo.RecordObjects(
            System.Array.ConvertAll(targets.ToArray(), t => (Object)t.transform),
            "Paste Positions");

        for (int i = 0; i < targets.Count; i++)
        {
            // Cycle through copied positions if fewer than targets
            Vector3 src = copiedPositions[i % copiedPositions.Count];
            Transform t = targets[i].transform;

            Vector3 current = useLocalSpace ? t.localPosition : t.position;

            Vector3 next = new Vector3(
                pasteX ? src.x : current.x,
                pasteY ? src.y : current.y,
                pasteZ ? src.z : current.z);

            if (useLocalSpace)
                t.localPosition = next;
            else
                t.position = next;
        }

        Debug.Log($"[PositionCopyPaste] Pasted to {targets.Count} object(s).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void DrawSeparator()
    {
        GUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        GUILayout.Space(2);
    }
}
