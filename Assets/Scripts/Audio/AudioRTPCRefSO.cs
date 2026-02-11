using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Wwise RTPC Ref", fileName = "RTPCRef_")]
public class AudioRTPCRefSO : ScriptableObject
{
    [Tooltip("Nom lisible (sert de 'label' dans Unity). Mets le même que ton state logique: Bus, Progression...")]
    public string label;

    [Tooltip("RTPC Wwise (picker).")]
    public AK.Wwise.RTPC rtpc;

}
