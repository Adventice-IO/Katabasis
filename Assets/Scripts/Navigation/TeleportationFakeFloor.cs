using UnityEngine;
using UnityEngine.InputSystem;

public class TeleportFloorVisuals : MonoBehaviour
{
    [Header("Assign in Inspector")]
    [Tooltip("The fake floor object you created")]
    public MeshRenderer floorRenderer;
    BoxCollider floorCollider;

    [Tooltip("The action that activates the Teleport Ray (e.g., Teleport Mode Activate)")]
    public InputActionReference teleportActivateAction;

    bool showingFloor = false;
    float alpha = 0;

    void Start()
    {
        // Ensure floor is invisible to start
        if (floorRenderer != null)
        {
            floorRenderer.enabled = false;
            floorCollider = floorRenderer.GetComponent<BoxCollider>();
            if (floorCollider != null)
            {
                floorCollider.enabled = false; // Disable collider to avoid interfering with teleportation raycasts
            }
        }

        showingFloor = false;
    }

    private void Update()
    {
        // Optional: You can add smooth fade-in/out logic here if desired
        if (floorRenderer == null) return;
        Color color = floorRenderer.material.color;
        if (showingFloor && alpha < 1f)
        {
            alpha += Time.deltaTime * 5f; // Fade in speed
            color.a = Mathf.Clamp01(alpha);
            floorRenderer.material.color = color;
        }
       
        if (!showingFloor && alpha > 0f)
        {
            alpha -= Time.deltaTime * 5f; // Fade out speed
            color.a = Mathf.Clamp01(alpha);
            floorRenderer.material.color = color;

            if (alpha <= 0f)
            {
                floorRenderer.enabled = false; // Fully invisible
                floorCollider.enabled = false; // Disable collider when floor is hidden
            }
        }
    }

    void OnEnable()
    {
        if (teleportActivateAction != null)
        {
            teleportActivateAction.action.started += ShowFloor;
            teleportActivateAction.action.canceled += HideFloor;
        }
    }

    void OnDisable()
    {
        if (teleportActivateAction != null)
        {
            teleportActivateAction.action.started -= ShowFloor;
            teleportActivateAction.action.canceled -= HideFloor;
        }
    }

    private void ShowFloor(InputAction.CallbackContext ctx)
    {
        if (floorRenderer == null) return;
        floorRenderer.enabled = true;
        floorCollider.enabled = true; // Enable collider when floor is visible
        showingFloor = true;
    }

    private void HideFloor(InputAction.CallbackContext ctx)
    {
        if (floorRenderer == null) return;
        showingFloor = false;
    }
}