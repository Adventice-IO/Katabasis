using System.Collections.Generic;
using UnityEngine;

public class AudioStateManager : MonoBehaviour
{
    [Header("States contrôlables")]
    public List<AudioStateRefSO> stateRefs = new List<AudioStateRefSO>();

    [Header("Default")]
    public AudioStateRefSO defaultState;

    [Header("Debug")]
    public bool logChanges = false;

    private uint _currentStateId = 0;

    private void Start()
    {
        if (defaultState != null)
            Set(defaultState);
    }

    public void Set(AudioStateRefSO stateRef)
    {
        if (stateRef == null) return;
        if (stateRef.state == null || stateRef.state.Id == 0) return;

        if (_currentStateId == stateRef.state.Id) return;

        stateRef.state.SetValue();
        _currentStateId = stateRef.state.Id;

        if (logChanges)
            Debug.Log($"[AudioStateManager] State set: {stateRef.label} ({stateRef.state.Name})");
    }

    // Si tu veux quand même pouvoir set directement un AK.Wwise.State
    public void Set(AK.Wwise.State state)
    {
        if (state == null || state.Id == 0) return;
        if (_currentStateId == state.Id) return;

        state.SetValue();
        _currentStateId = state.Id;

        if (logChanges)
            Debug.Log($"[AudioStateManager] State set: {state.Name}");
    }
}
