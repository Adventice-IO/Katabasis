using System;
using System.Collections.Generic;
using UnityEngine;

//[ExecuteInEditMode]
public class PointCloudMask : MonoBehaviour
{
    public List<Salle> visibleInSalles;
    public List<Tunnel> visibleInTunnels;
    [Range(0.1f, 30f)]
    public float animateTime = 1f;

    [Range(0, 1)]
    public float rawAlpha = 0.1f;
    [Range(0, 1)]
    public float alpha = 1.0f;

    public bool soloWhenInside = false;

    [Range(0, 1)]
    public float feather = 0.1f;

    public AnimationCurve alphaCurve;

    public MaskBox maskBox;

    MainController mainController;

    void Start()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
    }

    // Update is called once per frame
    void Update()
    {
        bool shouldBeVisible = false;
        if (mainController.salle != null && visibleInSalles.Contains(mainController.salle)) shouldBeVisible = true;
        if (mainController.tunnel != null && visibleInTunnels.Contains(mainController.tunnel)) shouldBeVisible = true;
        if (isCameraInsideMask()) shouldBeVisible = true;

        if (Application.isPlaying)
        {
            if (shouldBeVisible)
            {
                rawAlpha = Mathf.Min(1f, rawAlpha + Time.deltaTime / animateTime);
            }
            else
            {
                rawAlpha = Mathf.Max(0f, rawAlpha - Time.deltaTime / animateTime);
            }
        }

        alpha = alphaCurve.Evaluate(rawAlpha);
        maskBox = new MaskBox(transform.worldToLocalMatrix, Vector3.one / 2, alpha, feather, soloWhenInside ? 1f : 0f);
    }

    bool isCameraInsideMask()
    {
        if (Camera.main == null) return false;
        Vector3 localCamPos = transform.worldToLocalMatrix.MultiplyPoint(Camera.main.transform.position);
        return Mathf.Abs(localCamPos.x) < 0.5f && Mathf.Abs(localCamPos.y) < 0.5f && Mathf.Abs(localCamPos.z) < 0.5f;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, alpha, 0f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.color = new Color(1f, .3f, 0f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * (1 - feather));
    }
}
