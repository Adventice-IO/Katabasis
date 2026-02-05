using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Wwise State Ref", fileName = "StateRef_")]
public class AudioStateRefSO : ScriptableObject
{
    [Tooltip("Nom lisible (sert de 'label' dans Unity). Mets le même que ton state logique: Room_A, Inter_AB, etc.")]
    public string label;

    [Tooltip("State Wwise (picker).")]
    public AK.Wwise.State state;
}
