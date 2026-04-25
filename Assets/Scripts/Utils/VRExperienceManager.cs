using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

public class VRExperienceManager : MonoBehaviour
{
    private static readonly string[] ControllerTokens =
    {
        "controller",
        "teleport",
        "near-far interactor",
        "ray interactor"
    };

    private static readonly string[] HandTokens =
    {
        "left hand",
        "right hand",
        "hand visualizer",
        "hand tracking",
        "hand quest visual",
        "hand android xr visual"
    };

    public bool isInHeadset = false;
    public bool disableAllControllersInHeadset = true;
    public bool disableHandsInHeadset = true;
    public bool forceStandardSettingsInHeadset = true;

    public Salle defaultSalle;

    GameObject xrRoot;

    void Start()
    {
        xrRoot = gameObject;
        isInHeadset = XRSettings.isDeviceActive && !Application.isEditor;


        //Full log of environment
        Debug.Log($"VR Experience Manager initialized. Headset detected: {isInHeadset}");
        Debug.Log($"VR Experience XR Device Name: {XRSettings.loadedDeviceName}");
        Debug.Log($"VR Experience Editor: {Application.isEditor}");
        Debug.Log($"VR Experience Device Active: {XRSettings.isDeviceActive}");


        if (isInHeadset)
        {
            Debug.Log("Headset detected. Adjusting VR experience settings.");

            // if (disableAllControllersInHeadset)
            // {
            //     SetMatchingObjectsActive(ControllerTokens, false);
            // }

            // if (disableHandsInHeadset)
            // {
            //     SetMatchingObjectsActive(HandTokens, false);
            // }

            if (forceStandardSettingsInHeadset)
            {
                MainController mainController = GetComponent<MainController>();
                if (mainController != null)                
                {
                    mainController.initialSalle = defaultSalle;
                    mainController.globalSpeedMultiplier = 1.0f;
                    mainController.gameState = MainController.GameState.Menu;
                }
                // Code to apply standard settings for VR experience
            }
        }
    }



    private  void SetMatchingObjectsActive(string[] tokens, bool isActive)
    {

        Transform[] transforms = xrRoot.GetComponentsInChildren<Transform>(true);
        for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
        {
            Transform currentTransform = transforms[transformIndex];
            if (currentTransform == null || !MatchesAnyToken(currentTransform.name, tokens))
            {
                continue;
            }

            currentTransform.gameObject.SetActive(isActive);
        }
        
    }

    private bool MatchesAnyToken(string value, string[] tokens)
    {
        for (int index = 0; index < tokens.Length; index++)
        {
            if (value.IndexOf(tokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
