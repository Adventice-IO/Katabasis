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

    public AnimationCurve alphaCurve;

    public MaskBox maskBox;

    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        bool shouldBeVisible = false;
        if (MainController.instance.salle != null && visibleInSalles.Contains(MainController.instance.salle)) shouldBeVisible = true;
        if (MainController.instance.tunnel != null && visibleInTunnels.Contains(MainController.instance.tunnel)) shouldBeVisible = true;
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
        maskBox = new MaskBox(transform.worldToLocalMatrix, Vector3.one / 2, alpha);
    }

    bool isCameraInsideMask()
    {
        if(Camera.main == null) return false;
        Vector3 localCamPos = transform.worldToLocalMatrix.MultiplyPoint(Camera.main.transform.position);
        return Mathf.Abs(localCamPos.x) < 0.5f && Mathf.Abs(localCamPos.y) < 0.5f && Mathf.Abs(localCamPos.z) < 0.5f;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, alpha, 0f, 1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
