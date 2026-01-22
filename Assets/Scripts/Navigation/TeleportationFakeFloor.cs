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
    [Tooltip("The action that activates the Teleport Ray (e.g., Teleport Mode Activate)")]
    public InputActionReference spawnActivateAction;

    bool showingFloor = false;
    bool spawnMode = false;
    float alpha = 0;

    public Color teleportColor;
    public Color spawnColor;

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
        Color color = spawnMode ? spawnColor : teleportColor;
        if (showingFloor && alpha < 1f)
        {
            alpha += Time.deltaTime * 5f; // Fade in speed
            color.a = Mathf.Clamp01(alpha);
        }

        if (!showingFloor && alpha > 0f)
        {
            alpha -= Time.deltaTime * 5f; // Fade out speed
            color.a = Mathf.Clamp01(alpha);

            if (alpha <= 0f)
            {
                floorRenderer.enabled = false; // Fully invisible
                floorCollider.enabled = false; // Disable collider when floor is hidden
            }
        }

        floorRenderer.material.color = color;
    }

    void OnEnable()
    {
        if (teleportActivateAction != null)
        {
            teleportActivateAction.action.started += ShowFloor;
            teleportActivateAction.action.canceled += HideFloor;
        }

        if (spawnActivateAction != null)
        {
            spawnActivateAction.action.started += ShowFloor;
            spawnActivateAction.action.canceled += HideFloor;
        }

    }

    void OnDisable()
    {
        if (teleportActivateAction != null)
        {
            teleportActivateAction.action.started -= ShowFloor;
            teleportActivateAction.action.canceled -= HideFloor;
        }

        if (spawnActivateAction != null)
        {
            spawnActivateAction.action.started -= ShowFloor;
            spawnActivateAction.action.canceled -= HideFloor;
        }

    }

    private void ShowFloor(InputAction.CallbackContext ctx)
    {
        if (floorRenderer == null) return;
        floorRenderer.enabled = true;
        floorCollider.enabled = true; // Enable collider when floor is visible
        showingFloor = true;
        spawnMode = ctx.action == spawnActivateAction.action;
    }

    private void HideFloor(InputAction.CallbackContext ctx)
    {
        if (floorRenderer == null) return;
        showingFloor = false;
    }
}