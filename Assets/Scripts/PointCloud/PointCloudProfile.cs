using UnityEngine;

[CreateAssetMenu(fileName = "Point Cloud Profile", menuName = "ScriptableObjects/Point Cloud Profile", order = 1)]
public class PointCloudProfile : ScriptableObject
{
    public float fadeIn = 1f;
    public float fadeOut = 1f;
    public bool modePoint = true;
    [Range(0f, 1f)] public float _Alpha = 1f;
    public float _FadeDistanceRange = 20f;
    public float _FadeDistanceFeather = 5f;
    public float _DensityCropRange = 2f;
    public float _DensityCropFeather = 5f;
    public float _DensityCropMax = 0.9f;
}