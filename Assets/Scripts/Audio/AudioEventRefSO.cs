using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Wwise Event Ref", fileName = "EventRef_")]
public class AudioEventRefSO : ScriptableObject
{
    [Tooltip("Nom lisible (sert de 'label' dans Unity). Mets le même que ton state logique: Portal, Interview...")]
    public string label;

    [Tooltip("Event Wwise (picker).")]
    public AK.Wwise.Event evt;

}
