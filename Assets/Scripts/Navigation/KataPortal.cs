using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[ExecuteInEditMode]
public class KataPortal : MonoBehaviour
{

    Tunnel tunnel;
    Salle fromSalle;
    Salle toSalle;

    MainController mainController;
    XRSimpleInteractable interactable;
    VisualEffect vfx;
    Collider col;

    [Range(0f, 1f)]
    public float positionAlongTunnel = .01f;
    public float elevation = 0;

    public float focusTime = 3f;
    public float timeBeforeReveal = 3f;

    public bool isFocused { get; set; } = false;

    [Range(0, 1)]
    public float progression = 0f;

    bool showing = false;

    [Header("Audio Settings")]
    public AudioEventRefSO loadingEvent;
    public AudioEventRefSO validateEvent;
    public AudioRTPCRefSO progRTPC;


    public List<Salle> blacklist = new List<Salle>();

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
            if (transform.parent != null) tunnel = transform.parent.GetComponent<Tunnel>();
        }

        if (tunnel == null) return;

        transform.position = tunnel.getPositionOnTrack(positionAlongTunnel) + Vector3.up * elevation;

        bool isInTunnel = mainController.isInTunnel(tunnel);
        bool showInSalle = mainController.isTunnelACurrentOut(tunnel) && mainController.timeSinceArrived > timeBeforeReveal;
        bool showInTunnel = isInTunnel && (isFirst() ? mainController.trackPosition < .5f : mainController.trackPosition > .5f);
        bool shouldShow = showInSalle || showInTunnel;

        if (showInSalle)
        {
            if (mainController.comingFromTunnel == tunnel)
            {
                shouldShow = false;
            }
            else
            {
                foreach (Salle s in blacklist)
                {
                    if (mainController.hasVisitedSalle(s))
                    {
                        shouldShow = false;
                        break;
                    }
                }
            }

        }

        if (showing != shouldShow)
        {
            show(shouldShow);
        }

        if (Application.isPlaying && showing && !isInTunnel)
        {
            float focusProg = Time.deltaTime * (isFocused ? 1 : -1) / focusTime;

            float newProg = Mathf.Clamp01(progression + focusProg);

            if (newProg != progression)
            {
                if (newProg > 0 && progression == 0)
                {
                    loadingEvent.evt.Post(gameObject);
                }
                else if (newProg == 0 && progression > 0)
                {
                    loadingEvent.evt.Stop(gameObject);
                }

                progression = newProg;
                progRTPC.evt.SetValue(gameObject, progression);

                vfx.SetFloat("Progression", progression);
                if (progression >= 1f)
                {
                    mainController.GoToSalle(tunnel.getOtherSalle(mainController.salle));
                    loadingEvent.evt.Stop(gameObject);
                    validateEvent.evt.Post(gameObject);
                }
            }
        }

    }

    public void show(bool val)
    {
        progression = 0f;
        showing = val;

        GetComponent<VisualEffect>().enabled = showing;
        GetComponent<Collider>().enabled = showing;
    }

    public bool isFirst()
    {
        return positionAlongTunnel < .5f;
    }

}
