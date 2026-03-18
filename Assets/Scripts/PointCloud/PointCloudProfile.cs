using UnityEngine;

[CreateAssetMenu(fileName = "Point Cloud Profile", menuName = "ScriptableObjects/Point Cloud Profile", order = 1)]
public class PointCloudProfile : ScriptableObject
{
    public float fadeIn = 1f;
    public float fadeOut = 1f;
    [Range(0f, 1f)] public float _Alpha = 1f;
    public bool linkMaxDistanceToCamera = true;
    public float _MaxDistance = 20f;
    public float _DistanceMultiplier = 1f;
    [Range(0f, 1f)]
    public float _DistanceFade = .3f;


    [Header("Noise")]
    public float _NoiseAmplitude = .5f;
    public float _NoiseThickness = .5f;
    public float _NoiseScale = 1f;
    public float _NoiseAlphaMultiplier = 1f;
}