using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[ExecuteAlways]
public class KataPortal : MonoBehaviour
{

    Tunnel tunnel;
    MainController mainController;
    XRSimpleInteractable interactable;
    VisualEffect vfx;
    Collider col;

    [Range(0, 1)]
    public float positionAlongTunnel = .01f;
    public float elevation = 0;

    public float focusTime = 2f;
    public float timeBeforeReveal = 3f;

    public bool isFocused { get; set; }
    float progressiveFocusTime = 0f;

    bool showing = false;

    void OnEnable()
    {
        mainController = MainController.instance;
        tunnel = transform.parent.GetComponent<Tunnel>();
        vfx = GetComponent<VisualEffect>();
        col = GetComponent<Collider>();

        interactable = GetComponent<XRSimpleInteractable>();

    }

    void OnDisable()
    {
        if (interactable != null)
        {
            interactable.hoverEntered.RemoveAllListeners();
        }
    }

    void Start()
    {
        if (Application.isPlaying)
        {
            showing = false;
            vfx.enabled = false;
            col.enabled = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (mainController == null || tunnel == null)
        {
            mainController = MainController.instance;
            tunnel = transform.parent.GetComponent<Tunnel>();
        }

        transform.position = tunnel.getPositionOnTrack(positionAlongTunnel) + Vector3.up * elevation;

        bool isInTunnel = mainController.isInTunnel(tunnel);
        bool showInSalle = mainController.isTunnelACurrentOut(tunnel) && mainController.timeSinceArrived > timeBeforeReveal;
        bool showInTunnel = isInTunnel && (isFirst() ? mainController.trackPosition < .5f : mainController.trackPosition > .5f);
        bool shouldShow = showInSalle || showInTunnel;


        if (showing != shouldShow)
        {
            progressiveFocusTime = 0f;
            showing = shouldShow;

            GetComponent<VisualEffect>().enabled = showing;
            GetComponent<Collider>().enabled = showing;
        }

        if (Application.isPlaying && showing && !isInTunnel)
        {
            float focusProg = Time.deltaTime * (isFocused ? 1 : -1);

            progressiveFocusTime = Mathf.Clamp(progressiveFocusTime + focusProg, 0, focusTime);
            float relProgession = Mathf.Clamp01(progressiveFocusTime / focusTime);
            vfx.SetFloat("Progression", relProgession);
            if (relProgession >= 1f)
            {
                mainController.GoToSalle(tunnel.getOtherSalle(mainController.salle));
            }
        }

    }

    public bool isFirst()
    {
        return positionAlongTunnel < .5f;
    }

}
