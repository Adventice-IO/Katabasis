using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class KataPortal : MonoBehaviour
{

    Tunnel tunnel;
    Salle fromSalle;
    Salle toSalle;

    MainController mainController;
    XRSimpleInteractable interactable;
    VisualEffect vfx;
    Collider col;

    //[Range(0f, 1f)]
    //public float positionAlongTunnel = .01f;
    //public float elevation = 0;

    public float focusTime = 3f;
    public float timeBeforeReveal = 3f;

    public bool isFocused { get; set; } = false;

    [Range(0, 1)]
    public float progression = 0f;

    bool showing = true;

    public bool isReverse = false;

    [Header("Audio Settings")]
    public AudioEventRefSO loadingEvent;
    public AudioEventRefSO validateEvent;
    public AudioRTPCRefSO progRTPC;
    public bool debugAudio = false;

    public string portalName;


    public List<Salle> blacklist = new List<Salle>();


    void OnEnable()
    {
       
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
        mainController = GameObject.FindAnyObjectByType<MainController>();
        tunnel = transform.parent.GetComponent<Tunnel>();
        vfx = GetComponent<VisualEffect>();
        col = GetComponent<Collider>();

        if (Application.isPlaying)
        {
            showing = false;
            vfx.enabled = false;
            col.enabled = false;
        }

        

        //Debug.Log("KataPortal " + portalName + " start, tunnel: " + tunnel?.name);
    }

    // Update is called once per frame
    void Update()
    {
        if (mainController == null || tunnel == null)
        {
            mainController = GameObject.FindAnyObjectByType<MainController>();
            if (transform.parent != null) tunnel = GetComponentInParent<Tunnel>();
        }


        //Debug.Log("KataPortal " + portalName + " update, tunnel: " + (tunnel != null ? tunnel.name : "null") + ", mainController: " + (mainController != null ? mainController.name : "null"));
        if (tunnel == null) return;

        //transform.position = tunnel.getPositionOnTrack(positionAlongTunnel) + Vector3.up * elevation;


        bool shouldShow = true;
        bool isInTunnel = mainController.isInTunnel(tunnel);

        if (mainController.editMode)
        {
            shouldShow = isInTunnel || mainController.isTunnelACurrentOut(tunnel);
        }
        else
        {
            bool showInSalle = mainController.isTunnelACurrentOut(tunnel) && mainController.timeSinceArrived > timeBeforeReveal;
            bool showInTunnel = isInTunnel;
            if (isInTunnel)
            {
                if (isReverse)
                {
                    showInTunnel = mainController.isRunningReversed() && mainController.trackPosition < .5f;
                }
                else
                {
                    showInTunnel = !mainController.isRunningReversed() && mainController.trackPosition < .5f;
                }
            }

            if (mainController.infinitePlaying)
            {
                shouldShow = mainController.gameState == MainController.GameState.Playing
                    && IsEnabledForInfinitePlaying()
                    && (showInTunnel || showInSalle);
            }
            else
            {
                shouldShow = mainController.gameState == MainController.GameState.Playing && (showInTunnel || showInSalle);
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

                        //Do not show if the destination salle has already been visited
                        //Salle destSalle = isReverse ? tunnel.salleDepart : tunnel.salleArrivee;
                        //if (mainController.hasVisitedSalle(destSalle))
                        //{
                        //    shouldShow = false;
                        //}
                    }
                }
            }
        }

        if (mainController.infinitePlaying && !IsEnabledForInfinitePlaying())
        {
            shouldShow = false;
        }

        if (showing != shouldShow)
        {
            // Debug.Log($"KataPortal {portalName} should show: {shouldShow}");
            show(shouldShow);
        }

        if (Application.isPlaying && showing && !isInTunnel)
        {
            float focusProg = Time.deltaTime * (isFocused ? 1 : -1) / focusTime;

            float newProg = Mathf.Clamp01(progression + focusProg);

            if (mainController.editMode)
            {
                newProg = .2f; //force half progression in edit mode to see the portal effect without having to focus on it
            }


            if (newProg != progression)
            {
                if (newProg > 0 && progression == 0)
                {
                    if (debugAudio) Debug.Log("Posting loading event");
                    loadingEvent.evt?.Post(gameObject);
                }
                else if (newProg == 0 && progression > 0)
                {
                    if (debugAudio) Debug.Log("Stopping loading event");
                    loadingEvent.evt?.Stop(gameObject);
                }

                progression = newProg;
                progRTPC.rtpc.SetValue(gameObject, progression);

                vfx.SetFloat("Progression", progression);
                if (progression >= 1f)
                {
                    mainController.BeginRunStartCameraDiagnostics($"Portal validated portal='{portalName}' tunnel='{tunnel?.name}' reverse={isReverse}");
                    mainController.GoToSalle(tunnel.getOtherSalle(mainController.salle));
                    loadingEvent.evt.Stop(gameObject);
                    validateEvent.evt.Post(gameObject);
                }
            }
        }

    }

    bool IsEnabledForInfinitePlaying()
    {
        if (isReverse && IsTunnelBetween("A", "B"))
        {
            return false;
        }

        if (isReverse && IsTunnelBetween("B", "C"))
        {
            return false;
        }

        Salle destination = mainController.salle != null
            ? tunnel.getOtherSalle(mainController.salle)
            : (isReverse ? tunnel.salleDepart : tunnel.salleArrivee);
        return !mainController.hideExitPortalsInInfinitePlaying || destination == null || !destination.isExit;
    }

    bool IsTunnelBetween(string departureName, string arrivalName)
    {
        return tunnel.salleDepart != null
            && tunnel.salleArrivee != null
            && string.Equals(tunnel.salleDepart.name.Trim(), departureName, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(tunnel.salleArrivee.name.Trim(), arrivalName, System.StringComparison.OrdinalIgnoreCase);
    }

    public void show(bool val)
    {
        progression = 0f;
        showing = val;

        GetComponent<VisualEffect>().enabled = showing;
        GetComponent<Collider>().enabled = showing;
        GetComponent<VisualEffect>().SetFloat("Progression", progression);

        if(GetComponentInParent<KataTransformer>() != null)
        {
            GetComponentInParent<KataTransformer>().forceDisabled = !showing;
        }
    }

    //public bool isFirst()
    //{
    //    return positionAlongTunnel < .5f;
    //}

}
