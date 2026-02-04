using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform cameraTransform;

    [Header("Follow Options")]
    [Tooltip("Si désactivé, seul la position est suivie (utile pour ambisonique).")]
    public bool followRotation = true;

    [Header("Offsets (local caméra)")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    private void Reset()
    {
        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Position toujours suivie
        transform.position = cameraTransform.TransformPoint(positionOffset);

        // Rotation optionnelle
        if (followRotation)
        {
            transform.rotation =
                cameraTransform.rotation * Quaternion.Euler(rotationOffsetEuler);
        }
    }
}
