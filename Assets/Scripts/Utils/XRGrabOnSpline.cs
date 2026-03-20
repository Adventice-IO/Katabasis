using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

#if UNITY_EDITOR
using Framework.Utils.Editor;
#endif

[AddComponentMenu("XR/Custom/Spline Grab Interactable")]
public class SplineGrabInteractable : XRGrabInteractable
{
    [Header("Spline Settings")]
    public SplineContainer splineContainer;

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        // Ensure we use the 'Kinematic' movement type for smooth spline snapping
        movementType = MovementType.Kinematic;
    }

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);

        if (isSelected && updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
        {
            UpdatePositionOnSpline();
        }
    }

    private void UpdatePositionOnSpline()
    {
        if (splineContainer == null) return;


        // 2. Convert world position to spline local space
        float3 localPos = splineContainer.transform.InverseTransformPoint(transform.position);

        // 3. Find the nearest point on the spline (0.0 to 1.0)
        SplineUtility.GetNearestPoint(splineContainer.Spline, localPos, out float3 nearestLocalPos, out float t);

        // 4. Apply the position back to world space
        transform.position = splineContainer.transform.TransformPoint(nearestLocalPos);

#if UNITY_EDITOR
        UnityPlayModeSaver.SaveComponent(GetComponentInParent<CheckpointContainer>());
#endif

    }
}