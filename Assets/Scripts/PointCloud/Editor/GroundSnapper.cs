using BAPointCloudRenderer.CloudController;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class GroundSnapper
{
    public static bool enabled = false; // Set false to disable by default
    [Range(0.0f, 5.0f)]
    public static float horizontalSearch= 0.5f; // Distance threshold to trigger snapping
    [Range(0.0f, 5.0f)]
    public static float verticalSearch = 0.8f; // Distance threshold to trigger snapping
    public static int maxSearchRenderers = 5;

    static GroundSnapper()
    {
        EditorApplication.update += OnUpdate;
        Spline.Changed += OnSplineChanged;
    }

    private static void OnUpdate()
    {
        if (!enabled || Application.isPlaying || Selection.transforms.Length == 0) return;

        // Iterate over all selected transforms
        foreach (Transform t in Selection.transforms)
        {
            // Optimization: Only calculate if Unity detects a change
            if (t.hasChanged)
            {
                Vector3 currentPos = t.position;
                Vector3 snappedPos = GroundFinder.getGroundForPosition(currentPos, horizontalSearch, verticalSearch, maxSearchRenderers);

                if (Vector3.Distance(currentPos, snappedPos) > 0.001f)
                {
                    t.position = snappedPos;
                }

                t.hasChanged = false;
            }
        }
    }

    private static void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
    {
        if (!enabled) return;

        // Only run if the Knot position was modified
        if (modification != SplineModification.KnotModified) return;

        BezierKnot knot = spline[knotIndex];
        float3 localPos = knot.Position;

        SplineContainer container = null;

        // Check if the currently selected object has this spline
        if (Selection.activeGameObject != null)
        {
            var foundContainer = Selection.activeGameObject.GetComponent<SplineContainer>();
            if (foundContainer != null)
            {
                // Verify this container actually owns the spline we are editing
                // (A container can hold multiple splines)
                foreach (var s in foundContainer.Splines)
                {
                    if (s == spline)
                    {
                        container = foundContainer;
                        break;
                    }
                }
            }
        }

        if (container == null)
        {
            // Could not find a matching container in the selection, abort
            return;
        }

        Vector3 worldPos = container.transform.TransformPoint(localPos.x, localPos.y, localPos.z);
        Vector3 worldGroundPos = GroundFinder.getGroundForPosition(worldPos, horizontalSearch, verticalSearch, maxSearchRenderers);
        Vector3 localGroundPos = container.transform.InverseTransformPoint(worldGroundPos);
        float3 snappedPos = new float3(localGroundPos.x, localGroundPos.y, localGroundPos.z);

        // Infinite Loop Guard
        if (math.distance(localPos, snappedPos) < 0.001f)
        {
            return;
        }

        // Apply Snap
        knot.Position = snappedPos;
        spline[knotIndex] = knot;
    }
}


// 2. The Toolbar Button (Toggle)
[EditorToolbarElement(ID, typeof(SceneView))]
public class SnapToggle : EditorToolbarToggle
{
    public const string ID = "Katabasis/GroundSnapper";

    public SnapToggle()
    {
        value = GroundSnapper.enabled; // Initialize button state
        text = " S "; // Simple text icon. You can use 'icon = ...' for an image.
        tooltip = "Enable Custom Live Snapping";

        // Link the button state to our static boolean
        this.RegisterValueChangedCallback(evt =>
        {
            GroundSnapper.enabled = evt.newValue;
        });
    }
}

[EditorToolbarElement(ID, typeof(SceneView))]
public class SnapHThresholdField : EditorToolbarFloatField
{
    public const string ID = "Katabasis/HThreshold";
    public SnapHThresholdField()
    {
        text = " Horizontal Search ";
        tooltip = "Set Ground Snap Threshold Distance";
        // Initialize field value
        value = GroundSnapper.horizontalSearch;
        // Link the field value to our static float
        this.RegisterValueChangedCallback(evt =>
        {
            GroundSnapper.horizontalSearch = evt.newValue;
        });
    }
}

[EditorToolbarElement(ID, typeof(SceneView))]
public class SnapVThresholdField : EditorToolbarFloatField
{
    public const string ID = "Katabasis/VThreshold";
    public SnapVThresholdField()
    {
        text = " Horizontal Search ";
        tooltip = "Set Ground Snap Threshold Distance";
        // Initialize field value
        value = GroundSnapper.verticalSearch;
        // Link the field value to our static float
        this.RegisterValueChangedCallback(evt =>
        {
            GroundSnapper.verticalSearch = evt.newValue;
        });
    }
}

[EditorToolbarElement(ID, typeof(SceneView))]
public class SnapMaxRendererField : EditorToolbarFloatField
{
    public const string ID = "Katabasis/MaxRenderers";
    public SnapMaxRendererField()
    {
        text = " Max renderers ";
        tooltip = "Set Ground Snap Threshold Distance";
        // Initialize field value
        value = GroundSnapper.maxSearchRenderers;
        // Link the field value to our static float
        this.RegisterValueChangedCallback(evt =>
        {
            GroundSnapper.maxSearchRenderers= (int)evt.newValue;
        });
    }
}

// 3. The Overlay Panel (The container for the button)
[Overlay(typeof(SceneView), "Ground Snapping", true)]
public class SnapToolbarOverlay : ToolbarOverlay
{
    // Define the contents of this overlay
    SnapToolbarOverlay() : base(new string[] { SnapToggle.ID, SnapHThresholdField.ID, SnapVThresholdField.ID, SnapMaxRendererField.ID }) { }
}