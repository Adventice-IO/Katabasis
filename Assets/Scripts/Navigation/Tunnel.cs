using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using System.Linq;
using System;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using Framework.Utils.Editor;
using UnityEditor.Splines;
using UnityEditor.Splines.Editor;    // Required for SplineTool
#endif

[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class Tunnel : MonoBehaviour
{
    [Header("General Settings")]
    public Salle salleDepart;
    public Salle salleArrivee;

    [Header("Navigation Settings")]
    CheckpointContainer checkpointContainer;

    [Serializable]
    public struct PortalAudioData
    {
        [Range(0f, 1f)]
        public float position; // t along the spline (0..1)
        public AudioStateRefSO state; // audio state to trigger when passing through this portal
    }

    [Header("Audio Settings")]
    public AudioStateRefSO startSO;
    public List<PortalAudioData> audioPortals = new List<PortalAudioData>();
    public AudioEventRefSO audioEventSO;

    [Header("Manipulation")]
    public bool autoGroundKnots = true;
    public GameObject handlePrefab;
    public GameObject checkpointPrefab;

    [Header("Subtitles")]
    public string subtitlesPath;

    [Header("Spline")]
    public SplineContainer splineContainer;
    private bool lastWasSelected = false;

    LineRenderer lineRenderer;

    MainController mainController;

    List<KnotHandle> handles = new List<KnotHandle>();

    KataPortal portal;
    KataPortal portalReverse;


    List<CheckpointHandle> checkpointHandles = new List<CheckpointHandle>();

    public bool canReverse { get { return portalReverse != null; } }

    Spline spline
    {
        get
        {
            if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
            return splineContainer.Spline;
        }
    }

    const float EndpointSyncSqrTolerance = 0.000001f;




    [System.Serializable]
    public class SpeedCheckpoint
    {
        [Range(0f, 1f)] public float pos = 0.5f;
        [Tooltip("Desired speed multiplier t this point in m/s")]
        public float speed = 0.5f;
    }

    private void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();


        while (splineContainer.Spline.Count < 2)
        {
            //Add two knots if none exist
            splineContainer.Spline.Add(new BezierKnot(Vector3.zero));
        }



        checkpointContainer = GetComponent<CheckpointContainer>();
        lineRenderer = GetComponentInChildren<LineRenderer>();


    }

    private void OnEnable()
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (checkpointContainer == null) checkpointContainer = GetComponent<CheckpointContainer>();

#if UNITY_EDITOR
        Spline.Changed += OnSplineChanged;
#endif

        portal = transform.Find("Portal")?.GetComponentInChildren<KataPortal>();
        portalReverse = transform.Find("Portal Retour")?.GetComponentInChildren<KataPortal>();

        if (portal != null)
        {
            portal.isReverse = false;
            if (portal.transform.parent.localPosition == Vector3.zero)
            {
                portal.transform.parent.position = getPositionOnTrack(0.02f);
            }
        }

        if (portalReverse != null)
        {
            portalReverse.isReverse = true;
            if (portalReverse.transform.parent.localPosition == Vector3.zero)
            {
                portalReverse.transform.parent.position = getPositionOnTrack(0.98f);
            }
        }

    }

#if UNITY_EDITOR
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
            if (modificationType == SplineModification.KnotInserted || modificationType == SplineModification.KnotRemoved)
            {
                updateHandles();
            }
            UpdateLineRenderer();

        }

        //recalculate lineRenderer


    }
#endif

    private void Start()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();


#if UNITY_EDITOR
        UpdateLineRenderer();
        updateHandles();
        updateSpeedCheckpoints();
#else
      if (lineRenderer != null)
        {
            lineRenderer.enabled = false; // Disable line renderer in play mode; it's only for editing visualization
        }
#endif
    }

    void Update()
    {

        // if (spline.Count != handles.Count -2)
        // {
        //     updateHandles();
        // }

        //Update audio portals


        if (salleDepart == null || salleArrivee == null)
        {
            return;
        }

        SyncEndpointsToSalleOrigins();

        string n = $"{salleDepart?.name} > {salleArrivee?.name}";

        if (gameObject.name != n)
            gameObject.name = n;

        if (Application.isPlaying) lineRenderer.material.color = mainController.isInTunnel(this) ? Color.yellow : Color.white;


        if (portal != null && salleDepart != null)
        {
            Vector3 departLookAt = salleDepart.origin.position;
            departLookAt.y = portal.transform.parent.position.y;
            portal.transform.parent.LookAt(departLookAt, Vector3.up);
            portal.transform.LookAt(salleDepart.origin.position);
            portal.transform.Rotate(Vector3.up, 180); // Face the opposite direction since this is the reverse portal
        }

        if (portalReverse != null && salleArrivee != null)
        {
            Vector3 arriveeLookAt = salleArrivee.origin.position;
            arriveeLookAt.y = portalReverse.transform.parent.position.y;
            portalReverse.transform.parent.LookAt(arriveeLookAt, Vector3.up);
            portalReverse.transform.LookAt(salleArrivee.origin.position);
            portalReverse.transform.Rotate(Vector3.up, 180); // Face the opposite direction since this is the reverse portal
        }
    }


    public float getDesiredSpeedAtPosition(float t, bool reverse)
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return mainController.baseSpeed;


        Tuple<SpeedCheckpoint, SpeedCheckpoint> checkpoints = getSpeedCheckpointsAtPosition(t, false);//, reverse);

        if (checkpoints.Item1 == null && checkpoints.Item2 == null)
        {
            bool isNearEnd = reverse ? t < 0.2f : t > 0.8f;

            return isNearEnd ? 0.001f : mainController.baseSpeed;
        }


        float initSpeed = 0;
        float initPos = 0;
        float targetSpeed = 0.001f;
        float targetPos = 1;

        if (checkpoints != null)
        {
            if (checkpoints.Item1 != null)
            {
                initSpeed = checkpoints.Item1.speed;
                initPos = checkpoints.Item1.pos;
            }
            else if (checkpoints.Item2 != null)
            {
                initSpeed = checkpoints.Item2.speed;
            }

            if (checkpoints.Item2 != null)
            {
                targetSpeed = checkpoints.Item2.speed;
                targetPos = checkpoints.Item2.pos;
            }
        }

        float segmentLength = targetPos - initPos;
        if (segmentLength > 0.0001f)
        {
            float tSegment = (t - initPos) / segmentLength;
            float tSpeed = initSpeed + Mathf.Lerp(initSpeed, targetSpeed, tSegment);//mainController.speedCurve.Evaluate(tSegment));
            return Math.Max(tSpeed, 0.01f);
        }

        return initSpeed;
    }

    Tuple<SpeedCheckpoint, SpeedCheckpoint> getSpeedCheckpointsAtPosition(float trackPosition, bool reverse)
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return null;
        SpeedCheckpoint before = null;
        SpeedCheckpoint after = null;

        foreach (var checkpoint in checkpointContainer.speedCheckpoints)
        {
            if (checkpoint.pos <= trackPosition)
            {
                before = checkpoint;
            }
            else if (checkpoint.pos > trackPosition && after == null)
            {
                after = checkpoint;
            }
        }

        if (reverse) return Tuple.Create(after, before);
        return Tuple.Create(before, after);
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

    public float getNearestDistance(Vector3 position)
    {
        float3 localProj = splineContainer.transform.InverseTransformPoint(position);
        SplineUtility.GetNearestPoint(spline, localProj, out float3 nearestLocal, out float refinedT);

        return math.distance(localProj, nearestLocal);
    }

    public Vector3 getSplineForwardAtPosition(float trackPosition)
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return Vector3.forward;
        Vector3 tangent = splineContainer.EvaluateTangent(trackPosition);
        return tangent.normalized;
    }

    public void AddKnotAtPosition(Vector3 position, bool forceOnCurve = false)
    {
#if UNITY_EDITOR
        Debug.Log("Adding knot at position " + position + " (forceOnCurve=" + forceOnCurve + ")");
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
        float3 tangent = spline.EvaluateTangent(refinedT) / 8f;

        // max 1m for handles
        float maxHandleLength = 1.0f;
        if (math.length(tangent) > maxHandleLength)
        {
            tangent = math.normalize(tangent) * maxHandleLength;
        }
        float3 tanOut = tangent;
        float3 tanIn = -tanOut;
        // Keep planar tangents if your tunnels are intended to stay level.
        tanIn.y = 0;
        tanOut.y = 0;

        var newKnot = new BezierKnot(targetLocalPos, tanIn, tanOut);

        Undo.RecordObject(splineContainer, "Add Spline Knot");

        if (Application.isPlaying)
        {
            RuntimeUndoManager.addKnot(spline, index, newKnot);
            spline.SetTangentMode(index, TangentMode.Continuous);
        }
        else
        {

            try
            {
                spline.Insert(index, newKnot);
                spline.SetTangentMode(index, TangentMode.Continuous);
                Debug.Log("Inserted knot at index " + index);
            }
            catch
            {
                // Fallback to appending if Insert is unavailable
                spline.Add(newKnot);
                spline.SetTangentMode(spline.Count - 1, TangentMode.Continuous);
            }
        }

        // updateHandles();

        EditorUtility.SetDirty(splineContainer);
#endif

    }


    public void UpdateLineRenderer()
    {

#if UNITY_EDITOR
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

        lineRenderer.enabled = !Application.isPlaying || (mainController?.editMode == true);

        if(Application.isPlaying && !mainController.editMode) return;

        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return;
        List<Vector3> points = new List<Vector3>();
        float length = 0;
        Vector3 prevPos = Vector3.zero;
        for (float t = 0; t <= 1.0f; t += .005f)
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
            if (mainController.editMode)
            {
                lineRenderer.enabled = true;
                lineRenderer.material.SetFloat("_Width", lineRenderer.startWidth * lineRenderer.widthMultiplier);
                lineRenderer.material.SetFloat("_Length", length);
            }
            else
            {
                lineRenderer.enabled = false;
            }
        }
#endif
    }


    public void updateHandles()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;

        //Debug.Log($"{gameObject.name} Updating handles for spline with " + spline.Count + " knots");
        Transform handlesRoot = transform.Find("Handles");

        //Cleaner approach; remove ALL handles
        if (handlesRoot == null)
        {
            //Debug.LogWarning("Handles root not found, creating new one.");
            handlesRoot = new GameObject("Handles").transform;
            handlesRoot.parent = transform;
            handlesRoot.localPosition = Vector3.zero;
            handlesRoot.localRotation = Quaternion.identity;
        }

        while (handlesRoot.childCount > 0)
        {
            Transform child = handlesRoot.GetChild(0);
            DestroyImmediate(child.gameObject);
        }
        handles.Clear();

        if (!mainController.editMode) return;

        //Debug.Log("Cleared existing handles, spawning new ones for each knot (except endpoints)");

        for (int i = 0; i < spline.Count; i++)
        {
            if (i == 0 || i == spline.Count - 1)
            {
                // Debug.Log("Skipping handle for knot " + i + " since it's an endpoint");
                continue;
            }

            var knot = spline[i];
            Vector3 worldPos = splineContainer.transform.TransformPoint(knot.Position);
            GameObject handleObj = Instantiate(handlePrefab, worldPos, Quaternion.identity, handlesRoot);
            handleObj.name = "Handle_" + i;
            KnotHandle handle = handleObj.GetComponent<KnotHandle>();
            handles.Add(handle);

            handle.knotIndex = i;
            handle.splineContainer = splineContainer;
        }
#endif
    }

    public float getClosestTrackPosition(Vector3 position)
    {

        SplineUtility.GetNearestPoint(splineContainer.Spline, splineContainer.transform.InverseTransformPoint(position), out float3 nearestLocal, out float refinedT);
        return refinedT;
    }

    bool SyncEndpointToSalleOrigin(int knotIndex, Salle endpointSalle)
    {
        if (endpointSalle == null || endpointSalle.origin == null || splineContainer == null || splineContainer.Spline == null)
        {
            return false;
        }

        Vector3 localPos = splineContainer.transform.InverseTransformPoint(endpointSalle.origin.position);
        BezierKnot current = splineContainer.Spline[knotIndex];
        Vector3 currentPosition = new Vector3(current.Position.x, current.Position.y, current.Position.z);
        if ((currentPosition - localPos).sqrMagnitude <= EndpointSyncSqrTolerance)
        {
            return false;
        }

        splineContainer.Spline.SetKnot(knotIndex, new BezierKnot(localPos));
        return true;
    }

    public bool SyncEndpointsToSalleOrigins()
    {
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count == 0)
        {
            return false;
        }

        bool changed = false;
        changed |= SyncEndpointToSalleOrigin(0, salleDepart);
        changed |= SyncEndpointToSalleOrigin(splineContainer.Spline.Count - 1, salleArrivee);
        return changed;
    }

    public Vector3 getPositionOnTrack(float positionAlongTunnel)
    {
        return splineContainer.EvaluatePosition(positionAlongTunnel);
    }

    public Salle getOtherSalle(Salle salle)
    {
        return salle == salleDepart ? salleArrivee : salleDepart;
    }

    public AudioStateRefSO getAudioSOForPosition(float positionAlongTunnel)
    {
        AudioStateRefSO so = startSO;

        foreach (var portal in audioPortals)
        {
            if (positionAlongTunnel < portal.position)
            {
                break;
            }
            so = portal.state;
        }

        return so;
    }


    // CHECKPOINT

    public SpeedCheckpoint AddSpeedCheckpoint(float positionAlongTunnel)
    {
        SpeedCheckpoint checkpoint = new SpeedCheckpoint { pos = positionAlongTunnel, speed = 10 };
        checkpointContainer.speedCheckpoints.Add(checkpoint);
        checkpointContainer.speedCheckpoints = checkpointContainer.speedCheckpoints.OrderBy(c => c.pos).ToList();

        updateSpeedCheckpoints();
#if UNITY_EDITOR
        UnityPlayModeSaver.SaveComponent(checkpointContainer);
#endif
        return checkpoint;
    }

    public void RemoveSpeedCheckpoint(SpeedCheckpoint checkpoint)
    {
        checkpointContainer.speedCheckpoints.Remove(checkpoint);
#if UNITY_EDITOR
        UnityPlayModeSaver.SaveComponent(checkpointContainer);
#endif
        updateSpeedCheckpoints();
    }

    public void updateSpeedCheckpoints()
    {
#if UNITY_EDITOR

        if (!Application.isPlaying) return;

        //reorder checkpoints by position
        checkpointContainer.speedCheckpoints = checkpointContainer.speedCheckpoints.OrderBy(c => c.pos).ToList();


        Transform checkpointsRoot = transform.Find("Checkpoints");
        if (checkpointsRoot == null)
        {
            checkpointsRoot = new GameObject("Checkpoints").transform;
            checkpointsRoot.parent = transform;
            checkpointsRoot.localPosition = Vector3.zero;
            checkpointsRoot.localRotation = Quaternion.identity;
        }

        while (checkpointsRoot.childCount > 0)
        {
            Transform child = checkpointsRoot.GetChild(0);
            DestroyImmediate(child.gameObject);
        }

        checkpointHandles.Clear();

        if (!mainController.editMode) return;


        foreach (var checkpoint in checkpointContainer.speedCheckpoints)
        {
            Vector3 worldPos = splineContainer.EvaluatePosition(checkpoint.pos);
            GameObject handleObj = Instantiate(checkpointPrefab, worldPos, Quaternion.identity);
            handleObj.transform.position = worldPos;
            handleObj.transform.localScale = Vector3.one * 0.5f;
            handleObj.transform.parent = checkpointsRoot;
            handleObj.name = "Checkpoint_" + checkpoint.pos;
            CheckpointHandle handle = handleObj.GetComponentInChildren<CheckpointHandle>();
            handle.checkpoint = checkpoint;
            handle.tunnel = this;
            handle.init();
            checkpointHandles.Add(handle);
        }
#endif
    }


    // --- VISUALIZATION ONLY ---
    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;

        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || splineContainer.Spline == null) return;

#if UNITY_EDITOR
        bool isSelectedNow = UnityEditor.Selection.activeGameObject == this.gameObject;
        if (isSelectedNow != lastWasSelected)
        {
            lastWasSelected = isSelectedNow;
        }
#endif

        //Draw audio portals

        foreach (var portal in audioPortals)
        {

            Vector3 pos = splineContainer.EvaluatePosition(portal.position);
            Gizmos.color = Color.orange;

            //draw a plane perpendicular to the tunnel at this position
            Vector3 forward = splineContainer.EvaluateTangent(portal.position);
            Vector3 fNorm = forward.normalized;
            Vector3 up = Vector3.up; // could also use spline normal for more accuracy
            Vector3 right = Vector3.Cross(up, fNorm).normalized;
            float size = 5f;
            Vector3 center = pos;
            Vector3 corner1 = center + (right * size) + (up * size);
            Vector3 corner2 = center + (right * size) - (up * size);
            Vector3 corner3 = center - (right * size) - (up * size);
            Vector3 corner4 = center - (right * size) + (up * size);

            if (fNorm == Vector3.zero) fNorm = Vector3.forward; // fallback to avoid zero-length forward
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(fNorm, up), new Vector3(.5f, 1.0f, .1f));
            Gizmos.DrawWireSphere(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;

            //Draw audio icon
            Gizmos.DrawIcon(pos + Vector3.up * (size + 1.0f), "portal", true);
        }

        if (mainController == null)
        {
            mainController = GameObject.FindAnyObjectByType<MainController>();
            if (mainController == null)
            {
                Debug.LogWarning("Tunnel: MainController instance not found, cannot compute heatmap.");
                return;
            }
        }

        foreach (var checkpoint in checkpointContainer.speedCheckpoints)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawSphere(splineContainer.EvaluatePosition(checkpoint.pos), .4f);
        }
    }

    public float getPreviousSpeedCheckpointPosition(float trackPosition, bool isReversed)
    {
        SpeedCheckpoint checkpoint = checkpointContainer.speedCheckpoints.OrderByDescending(c => c.pos).FirstOrDefault(c => isReversed ? c.pos > trackPosition : c.pos < trackPosition);
        if (checkpoint != null)
        {
            return checkpoint.pos;
        }
        return isReversed ? 1f : 0f;

    }

    public float getNextSpeedCheckpointPosition(float trackPosition, bool isReversed)
    {
        SpeedCheckpoint checkpoint = checkpointContainer.speedCheckpoints.OrderBy(c => c.pos).FirstOrDefault(c => isReversed ? c.pos < trackPosition : c.pos > trackPosition);
        if (checkpoint != null)
        {
            return checkpoint.pos;
        }

        return isReversed ? 0f : 1f;
    }
}