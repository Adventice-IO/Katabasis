using System;
using System.Collections.Generic;
using UnityEngine;

public class PointCloudMask : MonoBehaviour
{
    public List<Salle> visibleInSalles;
    public List<Tunnel> visibleInTunnels;
    public float animateSpeed = 1f;
    [Range(0,1)]
    public float alpha = 1.0f;

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

        if (shouldBeVisible)
        {
            alpha = Mathf.Min(1f, alpha + Time.deltaTime * animateSpeed);
        }
        else
        {
            alpha = Mathf.Max(0f, alpha - Time.deltaTime * animateSpeed);
        }

        maskBox = new MaskBox(transform.worldToLocalMatrix, Vector3.one/ 2, alpha);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, alpha, 0f, 1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
