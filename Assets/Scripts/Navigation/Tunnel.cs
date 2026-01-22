using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Splines;
using UnityEditor;
using System.Linq;
using System;
using Framework.Utils.Editor;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;







#if UNITY_EDITOR
using UnityEditor.EditorTools; // Required for ToolManager
using UnityEditor.Splines.Editor;    // Required for SplineTool
#endif

[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class Tunnel : MonoBehaviour
{
    [Header("General Settings")]
    public Salle salleDepart;
    public Salle salleArrivee;

    [Header("Comfort Settings")]
    [Tooltip("0 = No slowdown at corners. 1 = Massive slowdown.")]
    [Range(0f, 1f)]
    public float cornerSlowdown = 0.6f;
    [Tooltip("How sensitive the system is to curves.")]
    [Range(0.001f, 0.1f)]
    public float curvatureSensitivity = .01f;

    [Header("Curvature Smoothing")]
    [Tooltip("How far (in normalized spline parameter) to sample curvature when computing a smoothed curvature value.")]
    [Range(0f, 0.1f)]
    public float curvatureSampleRadius = 0.01f;
    [Tooltip("How many samples (odd) to take across the sample radius for smoothing. Higher -> smoother but more expensive.")]
    [Range(1, 21)]
    public int curvatureSampleCount = 5;

    [Header("Manual Triggers")]
    public List<ManualSlowdown> manualSlowdowns = new List<ManualSlowdown>();

    [Header("Manipulation")]
    public bool autoGroundKnots = true;
    public GameObject handlePrefab;

    [Header("Editor Viz")]
    public bool showHeatmap = true;
    [Range(0.005f, 0.01f)]
    public float vizResolution = 0.01f;
    [Range(0.01f, 0.1f)]
    public float previzResolution = 0.05f;
    [Range(0.0001f, 0.01f)]
    public float lineResolution = 0.0005f;
    private SplineContainer splineContainer;
    private Spline spline;

    // --- Heatmap cache ---
    private int lastHeatmapHash = 0;
    private List<Vector3> cachedHeatPositions = new List<Vector3>();
    private List<float> cachedHeatValues = new List<float>();
    private bool heatmapDirty = true;
    private bool pendingHeatmapOnEditEnd = false;
    private bool lastWasSelected = false;

    LineRenderer lineRenderer;

    MainController MainController;

    int splineLastCount = 0;

    [System.Serializable]
    public class ManualSlowdown
    {
        [Range(0f, 1f)] public float position = 0.5f;
        public float radius = 5f;
        [Range(0f, 1f)] public float strength = 0.5f;
    }

    private void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();

        while (splineContainer.Spline.Count < 2)
        {
            //Add two knots if none exist
            splineContainer.Spline.Add(new BezierKnot(Vector3.zero));
        }


        spline = splineContainer.Spline;

        splineLastCount = spline.Count;

        lineRenderer = GetComponentInChildren<LineRenderer>();
        UpdateLineRenderer();
        updateHandles();

        MainController = FindFirstObjectByType<MainController>();

        if (Application.isPlaying)
        {
            UnityPlayModeSaver.SaveComponent(splineContainer);
            UnityPlayModeSaver.SaveComponent(this);
        }
    }

    private void OnEnable()
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        Spline.Changed += OnSplineChanged;
        UpdateLineRenderer();
        updateHandles();

    }



    private void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
    }

    private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modificationType)
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || spline == null) return;
        if (spline == splineContainer.Spline)
        {
            // Defer regeneration when spline changes; mark pending. Actual rebuild occurs when editing stops
            pendingHeatmapOnEditEnd = true;
        }

        //recalculate lineRenderer

        UpdateLineRenderer();
        updateHandles();
    }

    private void OnValidate()
    {
        // Clamp sensible ranges and mark heatmap dirty when inspector values change
        curvatureSampleRadius = Mathf.Clamp(curvatureSampleRadius, 0f, 0.5f);
        curvatureSampleCount = Mathf.Max(1, curvatureSampleCount);
        if ((curvatureSampleCount % 2) == 0) curvatureSampleCount += 1;
        vizResolution = Mathf.Clamp(vizResolution, 0.001f, 0.01f);
        previzResolution = Mathf.Clamp(previzResolution, 0.01f, 0.1f);
        heatmapDirty = true;
    }

    private void Update()
    {

        if (spline.Count != splineLastCount)
        {
            splineLastCount = spline.Count;
            updateHandles();
        }

        if (salleDepart == null || salleArrivee == null)
        {
            return;
        }

        if (salleDepart != null && splineContainer != null)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(salleDepart.origin.position);
            var current = splineContainer.Spline[0];
            Vector3 curPos = new Vector3(current.Position.x, current.Position.y, current.Position.z);
            if ((curPos - localPos).sqrMagnitude > 1e-6f)
                splineContainer.Spline.SetKnot(0, new BezierKnot(localPos));
        }
        if (salleArrivee != null && splineContainer != null)
        {
            int last = splineContainer.Spline.Count - 1;
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(salleArrivee.origin.position);
            var current = splineContainer.Spline[last];
            Vector3 curPos = new Vector3(current.Position.x, current.Position.y, current.Position.z);
            if ((curPos - localPos).sqrMagnitude > 1e-6f)
                splineContainer.Spline.SetKnot(last, new BezierKnot(localPos));
        }

        gameObject.name = $"{salleDepart?.name} > {salleArrivee?.name}";

        if (Application.isPlaying) lineRenderer.material.color = MainController.salle == null && MainController.tunnel == this ? Color.yellow : Color.white;

    }


    // PLAN / PSEUDOCODE:
    // 1. Keep ComputeBaseSpeed(tLocal) as before (curvature + manual zones).
    // 2. Choose a deltaT (based on vizResolution with sensible clamps).
    // 3. Compute tPrev = clamp01(t - deltaT), tNext = clamp01(t + deltaT).
    // 4. If tPrev was clamped to 0 (i.e. no valid previous sample), treat the "previous" desired speed as minSpeed
    //    and use the spline start position for distance calculations.
    // 5. If tNext was clamped to 1 (i.e. no valid next sample), treat the "next" desired speed as minSpeed
    //    and use the spline end position for distance calculations.
    // 6. Compute base speeds for the (possibly overridden) neighbor points.
    // 7. Compute world distances dsPrev, dsNext from current pos to neighbor pos (use EvaluatePosition(0/1) when clamped).
    // 8. Use kinematic formulas:
    //      vLimitFromPrev = sqrt(max(0, v_prev^2 + 2 * acceleration * dsPrev))
    //      vLimitForNext = sqrt(max(0, v_next^2 + 2 * deceleration * dsNext))
    //    (compute even when ds == 0 so start/end behavior is applied correctly)
    // 9. finalSpeed = min(baseSpeedAtT, vLimitFromPrev, vLimitForNext), clamp to [minSpeed, maxSpeed] and guard NaNs.
    // 10. Return finalSpeed.
    public float GetTargetSpeedAt(float t, float minSpeed, float maxSpeed, float acceleration, float deceleration)
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return minSpeed;

        // Local helper to compute the base (curvature + manual) speed at an arbitrary tLocal
        float ComputeBaseSpeed(float tLocal)
        {
            // Use a smoothed curvature value sampled across a small radius to avoid overly sharp responses
            float ComputeSmoothedCurvature(float tCenter)
            {
                int count = Mathf.Max(1, curvatureSampleCount);
                if ((count % 2) == 0) count += 1; // ensure odd

                float radius = Mathf.Clamp(curvatureSampleRadius, 0f, 0.5f);
                if (radius <= 0f || count == 1)
                {
                    float c = splineContainer.Spline.EvaluateCurvature(tCenter);
                    return (float.IsNaN(c) || float.IsInfinity(c)) ? 0f : c;
                }

                float sum = 0f;
                int valid = 0;
                for (int i = 0; i < count; ++i)
                {
                    float u = (float)i / (float)(count - 1); // 0..1
                    float offset = Mathf.Lerp(-radius, radius, u);
                    float sampleT = Mathf.Clamp01(tCenter + offset);
                    float c = splineContainer.Spline.EvaluateCurvature(sampleT);
                    if (float.IsNaN(c) || float.IsInfinity(c)) c = 0f;
                    sum += c;
                    valid++;
                }
                return (valid > 0) ? (sum / valid) : 0f;
            }

            float curvature = ComputeSmoothedCurvature(tLocal);
            if (float.IsNaN(curvature) || float.IsInfinity(curvature))
            {
                curvature = 0f;
            }
            float curveFactor = Mathf.Clamp01(curvature / curvatureSensitivity);
            float curvatureMultiplier = 1f - (curveFactor * cornerSlowdown);

            float manualMultiplier = 1f;
            Vector3 pointPosLocal = splineContainer.EvaluatePosition(tLocal);

            foreach (var zone in manualSlowdowns)
            {
                Vector3 zonePos = splineContainer.EvaluatePosition(zone.position);
                float dist = Vector3.Distance(pointPosLocal, zonePos);

                if (dist < zone.radius && zone.radius > 0f)
                {
                    float influence = 1f - (dist / zone.radius); // 0 at edge, 1 at center
                    manualMultiplier = Mathf.Min(manualMultiplier, 1f - (influence * zone.strength));
                }
            }

            float baseSpeedLocal = maxSpeed * curvatureMultiplier * manualMultiplier;
            if (float.IsNaN(baseSpeedLocal) || float.IsInfinity(baseSpeedLocal)) baseSpeedLocal = minSpeed;
            // Ensure we never go below the configured minimum speed
            baseSpeedLocal = Mathf.Clamp(baseSpeedLocal, minSpeed, maxSpeed);
            return baseSpeedLocal;
        }

        // 1. Compute base speed at t
        float baseSpeed = ComputeBaseSpeed(t);

        // 2. Sample neighboring parameter offsets
        float deltaT = 0.005f;
        float rawPrev = t - deltaT;
        float rawNext = t + deltaT;
        float tPrev = Mathf.Clamp01(rawPrev);
        float tNext = Mathf.Clamp01(rawNext);

        // Prepare start/end positions for boundary handling
        Vector3 posStart = splineContainer.EvaluatePosition(0f);
        Vector3 posEnd = splineContainer.EvaluatePosition(1f);

        // 3/4. Determine neighbor base speeds and positions.
        // Detect whether the raw neighbors were clamped by checking rawPrev/rawNext.
        float basePrev;
        Vector3 posPrev;
        if (rawPrev < 0f)
        {
            // previous sample was clamped to start -> use minimum allowed speed as the neighbor
            basePrev = Mathf.Max(minSpeed, 0f);
            posPrev = posStart;
            tPrev = 0f;
        }
        else
        {
            basePrev = ComputeBaseSpeed(tPrev);
            posPrev = splineContainer.EvaluatePosition(tPrev);
        }

        // Arrival target: arrive "very slow" (minSpeed) at the end.
        // This makes the slowdown behavior depend on deceleration: lower decel => earlier slowdown.
        float baseNext = Mathf.Max(0f, minSpeed);
        Vector3 posNext = posEnd;
        tNext = 1f;

        // 4. Compute arc/world distances to neighbors by sampling along the spline (better approximation than straight-line)
        Vector3 pos = splineContainer.EvaluatePosition(t);

        float DistanceAlongSpline(float a, float b)
        {
            if (Mathf.Approximately(a, b)) return 0f;
            float start = Mathf.Clamp01(a);
            float end = Mathf.Clamp01(b);
            // ensure iteration from smaller to larger
            if (end < start)
            {
                float tmp = start; start = end; end = tmp;
            }

            float step = Mathf.Clamp(vizResolution, 0.001f, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt((end - start) / step));
            float sum = 0f;
            Vector3 prev = splineContainer.EvaluatePosition(start);
            for (int i = 1; i <= steps; ++i)
            {
                float u = (float)i / (float)steps;
                float tt = Mathf.Lerp(start, end, u);
                Vector3 p = splineContainer.EvaluatePosition(tt);
                sum += Vector3.Distance(prev, p);
                prev = p;
            }
            return sum;
        }

        // Always compute distance to start/end, so acceleration/deceleration can act well before start/arrival.
        float dsFromStart = DistanceAlongSpline(0f, t);
        // Always compute braking distance to the end, so deceleration can start well before arrival.
        float dsNext = DistanceAlongSpline(t, 1f);

        // 5. Kinematic limits:
        // From previous point (acceleration): v^2 <= v_prev^2 + 2 * a * ds
        // Start acceleration constraint: v^2 <= v_start^2 + 2 * accel * distance_from_start
        float vLimitFromStart = float.PositiveInfinity;
        {
            float vStart = Mathf.Max(0f, minSpeed);
            float sq = vStart * vStart + 2f * Mathf.Max(0f, acceleration) * Mathf.Max(0f, dsFromStart);
            vLimitFromStart = Mathf.Sqrt(Mathf.Max(0f, sq));
        }

        // For end-of-spline arrival (deceleration):
        // v^2 <= v_end^2 + 2 * decel * dsNext  (ensure we can brake in time to reach baseNext at t=1)
        float vLimitForNext = float.PositiveInfinity;
        {
            float sq = baseNext * baseNext + 2f * Mathf.Max(0f, deceleration) * Mathf.Max(0f, dsNext);
            vLimitForNext = Mathf.Sqrt(Mathf.Max(0f, sq));
        }

        // 6. Final speed is the most restrictive of base and kinematic limits
        float finalSpeed = Mathf.Min(baseSpeed, vLimitFromStart, vLimitForNext);

        // Safety clamps
        if (float.IsNaN(finalSpeed) || float.IsInfinity(finalSpeed)) finalSpeed = minSpeed;
        finalSpeed = Mathf.Clamp(finalSpeed, minSpeed, maxSpeed);

        return finalSpeed;
    }



    // --- VISUALIZATION ONLY ---
    private void OnDrawGizmos()
    {
        if (!showHeatmap) return;
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return;

#if UNITY_EDITOR
        bool isSelectedNow = UnityEditor.Selection.activeGameObject == this.gameObject;
        if (isSelectedNow != lastWasSelected)
        {
            lastWasSelected = isSelectedNow;
            heatmapDirty = true;
        }
#endif

        if (MainController == null) return;

        // If we have a pending heatmap requested during spline editing, only apply it after editing stops
        if (pendingHeatmapOnEditEnd)
        {
            // Only trigger rebuild once editing has definitely stopped.
            // Consider editing stopped when neither a SplineLiveModifier reports editing nor the selection contains the spline.
#if UNITY_EDITOR

            var selGO = UnityEditor.Selection.activeGameObject;
            bool selectedSpline = false;
            if (selGO != null)
            {
                var selSpline = selGO.GetComponent<SplineContainer>();
                if (selSpline == splineContainer) selectedSpline = true;
            }
            bool editingSpline = typeof(SplineTool).IsAssignableFrom(ToolManager.activeToolType);
            if (!(editingSpline && selectedSpline))
            {
                heatmapDirty = true;
                pendingHeatmapOnEditEnd = false;
            }
            else
            {
                //Debug.Log("Still editing spline, skipping heatmap regeneration this frame.");
                return;
            }
#endif
        }

        // Compute a stable hash of the inputs that influence the heatmap.
        // Quantize floats to avoid tiny per-frame floating point differences causing rebuilds.
        int hash = 17;
        static int CombineHash(int h, int v) { unchecked { return h * 23 + v; } }
        static int QuantizeFloat(float v, float scale = 100f) { return Mathf.RoundToInt(v * scale); }

        // coarser quantization to avoid tiny FP jitter causing cache misses
        hash = CombineHash(hash, QuantizeFloat(MainController.minSpeed, 100));
        hash = CombineHash(hash, QuantizeFloat(MainController.maxSpeed, 100));
        hash = CombineHash(hash, QuantizeFloat(cornerSlowdown, 100));
        hash = CombineHash(hash, QuantizeFloat(curvatureSensitivity, 10));
        hash = CombineHash(hash, QuantizeFloat(curvatureSampleRadius, 1000));
        hash = CombineHash(hash, curvatureSampleCount);
        hash = CombineHash(hash, QuantizeFloat(vizResolution, 10000));
        hash = CombineHash(hash, QuantizeFloat(MainController.acceleration, 100));
        hash = CombineHash(hash, QuantizeFloat(MainController.deceleration, 100));

        // include manual slowdown zones
        hash = CombineHash(hash, manualSlowdowns.Count);
        foreach (var z in manualSlowdowns)
        {
            hash = CombineHash(hash, QuantizeFloat(z.position));
            hash = CombineHash(hash, QuantizeFloat(z.radius));
            hash = CombineHash(hash, QuantizeFloat(z.strength));
        }

        // include spline knot positions and handles (tangents/rotation)
        hash = CombineHash(hash, splineContainer.Spline.Count);
        for (int i = 0; i < splineContainer.Spline.Count; ++i)
        {
            var knot = splineContainer.Spline[i];
            var p = knot.Position;
            hash = CombineHash(hash, QuantizeFloat(p.x));
            hash = CombineHash(hash, QuantizeFloat(p.y));
            hash = CombineHash(hash, QuantizeFloat(p.z));
            var tin = knot.TangentIn;
            var tout = knot.TangentOut;
            hash = CombineHash(hash, QuantizeFloat(tin.x));
            hash = CombineHash(hash, QuantizeFloat(tin.y));
            hash = CombineHash(hash, QuantizeFloat(tin.z));
            hash = CombineHash(hash, QuantizeFloat(tout.x));
            hash = CombineHash(hash, QuantizeFloat(tout.y));
            hash = CombineHash(hash, QuantizeFloat(tout.z));
            var rot = knot.Rotation.value; // float4 from Unity.Mathematics.quaternion
            hash = CombineHash(hash, QuantizeFloat(rot.x));
            hash = CombineHash(hash, QuantizeFloat(rot.y));
            hash = CombineHash(hash, QuantizeFloat(rot.z));
            hash = CombineHash(hash, QuantizeFloat(rot.w));
        }

        // Regenerate cache if needed
        //Debug.Log(heatmapDirty ? "Heatmap dirty" : "Heatmap clean");
        bool needRegen = heatmapDirty || hash != lastHeatmapHash || cachedHeatPositions.Count == 0;
        if (needRegen)
        {
            //Debug.Log("Regenerating Tunnel Heatmap Visualization Cache");
            cachedHeatPositions.Clear();
            cachedHeatValues.Clear();

            Vector3 prevPos = splineContainer.EvaluatePosition(0);
            float resolution = UnityEditor.Selection.activeGameObject == gameObject ? vizResolution : previzResolution;
            for (float t = 0; t < 1.0f; t += resolution)
            {
                Vector3 currentPos = splineContainer.EvaluatePosition(t);
                float targetSpeed = GetTargetSpeedAt(t, MainController.minSpeed, MainController.maxSpeed, MainController.acceleration, MainController.deceleration);

                // Color: Red = Slow, standard color when fast
                float ratio = Mathf.Clamp01(targetSpeed / MainController.maxSpeed);

                cachedHeatPositions.Add(prevPos);
                cachedHeatPositions.Add(currentPos);
                cachedHeatValues.Add(ratio);

                prevPos = currentPos;
            }

            // Ensure the last segment reaches t=1 so end-of-spline braking is reflected in the heatmap
            {
                float t = 1.0f;
                Vector3 currentPos = splineContainer.EvaluatePosition(t);
                // At exact end we want to arrive very slow (minSpeed)
                float targetSpeed = GetTargetSpeedAt(t, MainController.minSpeed, MainController.maxSpeed, MainController.acceleration, MainController.deceleration);
                float ratio = Mathf.Clamp01(targetSpeed / MainController.maxSpeed);

                cachedHeatPositions.Add(prevPos);
                cachedHeatPositions.Add(currentPos);
                cachedHeatValues.Add(ratio);
            }

            lastHeatmapHash = hash;
            heatmapDirty = false;
        }

        // Draw cached heatmap
        Color stdColor = (UnityEditor.Selection.activeGameObject == this.gameObject) ? Color.green : Color.cyan;
        for (int i = 0; i < cachedHeatPositions.Count; i += 2)
        {
            Gizmos.color = Color.Lerp(Color.red, stdColor, cachedHeatValues[i / 2]);
            Gizmos.DrawLine(cachedHeatPositions[i], cachedHeatPositions[i + 1]);
        }
    }


    public void SpawnKnot(SelectEnterEventArgs args)
    {
        if (args == null)
        {
            return;
        }

        IXRRayProvider rayProvider = args.interactorObject as IXRRayProvider;

        if (rayProvider != null)
        {
            AddKnotAtPosition(rayProvider.rayEndPoint);
        }
    }

    public void AddKnotAtPosition(Vector3 position, bool forceOnCurve = false)
    {
        float3 localProj = splineContainer.transform.InverseTransformPoint(position);
        SplineUtility.GetNearestPoint(spline, localProj, out float3 nearestLocal, out float refinedT);

        int index = 0;

        // Compute insertion index from refinedT
        int curveCount = 0;
        try
        {
            curveCount = spline.GetCurveCount();
        }
        catch
        {
            curveCount = Mathf.Max(1, spline.Count - 1);
        }

        if (curveCount <= 0)
        {
            return;
        }

        var knots = spline.ToList();
        for (var i = 0; i < knots.Count; i++)
        {
            SplineUtility.GetNearestPoint(spline, knots[i].Position, out float3 nearestLocalKnot, out float refinedKnot);
            if (refinedT < refinedKnot)
            {
                index = i;
                break;
            }
        }


        // Build knot data at refined position
        Vector3 targetLocalPos = forceOnCurve ? nearestLocal : splineContainer.transform.InverseTransformPoint(position);
        Vector3 targetWorldPos = splineContainer.transform.TransformPoint((Vector3)nearestLocal);

        // Use local-space tangent at the refined insertion parameter and convert
        // the derivative to Bezier handle length (derivative is ~3 * handle vector).
        float3 tangent = spline.EvaluateTangent(refinedT);

        float3 tanOut = tangent / 8f;
        float3 tanIn = -tanOut;
        // Keep planar tangents if your tunnels are intended to stay level.
        tanIn.y = 0;
        tanOut.y = 0;

        var newKnot = new BezierKnot(targetLocalPos, tanIn, tanOut);

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer, "Add Spline Knot");
#endif

        if (Application.isPlaying)
        {
            RuntimeUndoManager.addKnot(spline, index, newKnot);
        }
        else
        {

            try
            {
                spline.Insert(index, newKnot);
                Debug.Log("Inserted knot at index " + index);
            }
            catch
            {
                // Fallback to appending if Insert is unavailable
                spline.Add(newKnot);
                spline.SetTangentMode(spline.Count - 1, TangentMode.Continuous);
            }
        }

        updateHandles();

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer);
#endif

    }


    void UpdateLineRenderer()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
                lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                lineRenderer.widthMultiplier = 0.1f;
                lineRenderer.positionCount = 0;
                lineRenderer.loop = false;
                lineRenderer.useWorldSpace = true;
                lineRenderer.startColor = Color.white;
                lineRenderer.endColor = Color.white;
            }
        }
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return;
        List<Vector3> points = new List<Vector3>();
        float resolution = lineResolution;
        float length = 0;
        Vector3 prevPos = Vector3.zero;
        for (float t = 0; t <= 1.0f; t += resolution)
        {
            Vector3 pos = splineContainer.EvaluatePosition(t);
            points.Add(pos);
            if (prevPos != Vector3.zero)
            {
                length += Vector3.Distance(pos, prevPos);
            }
            prevPos = pos;
        }
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());

        if (Application.isPlaying)
        {
            lineRenderer.material.SetFloat("_Width", lineRenderer.startWidth * lineRenderer.widthMultiplier);
            lineRenderer.material.SetFloat("_Length", length);
        }

    }

    public void updateHandles()
    {
        if (!Application.isPlaying) return;

        Transform handlesRoot = transform.Find("Handles");
        if (handlesRoot == null)
        {
            handlesRoot = new GameObject("Handles").transform;
            handlesRoot.parent = transform;
            handlesRoot.localPosition = Vector3.zero;
            handlesRoot.localRotation = Quaternion.identity;
            handlesRoot.localScale = Vector3.one;
        }

        while (handlesRoot.childCount > spline.Count)
        {
            Transform child = handlesRoot.GetChild(handlesRoot.childCount - 1);
            DestroyImmediate(child.gameObject);
        }

        for (int i = 0; i < handlesRoot.childCount; i++)
        {
            Transform child = handlesRoot.GetChild(i);
            KnotHandle handle = child.GetComponent<KnotHandle>();
            if (handle != null)
            {
                handle.knotIndex = i;
                handle.gameObject.name = "Handle_" + i;
                handle.updateActive();
            }
        }

        int curChildCount = handlesRoot.childCount;


        while (curChildCount < spline.Count)
        {
            var knot = spline[curChildCount];
            Vector3 worldPos = splineContainer.transform.TransformPoint(knot.Position);
            Transform handleTransform = handlesRoot.Find("Handle_" + curChildCount);
            KnotHandle handle = null;
            if (handleTransform == null)
            {
                GameObject handleObj = Instantiate(handlePrefab, worldPos, Quaternion.identity, handlesRoot);
                handleObj.name = "Handle_" + curChildCount;
                handleTransform = handleObj.transform;
                handle = handleObj.GetComponent<KnotHandle>();

                handle.knotIndex = curChildCount;
                handle.splineContainer = splineContainer;

            }
            else
            {
            }


            curChildCount++;
        }

        KnotHandle[] allHandles = handlesRoot.GetComponentsInChildren<KnotHandle>();
        for (int i = 0; i < allHandles.Length; i++)
        {
            allHandles[i].updateActive();
        }

    }

    public float getClosestTrackPosition(Vector3 position)
    {

        SplineUtility.GetNearestPoint(splineContainer.Spline, splineContainer.transform.InverseTransformPoint(position), out float3 nearestLocal, out float refinedT);
        return refinedT;
    }
}