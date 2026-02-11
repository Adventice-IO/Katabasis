using UnityEngine;

public class WwiseStateRemote : MonoBehaviour
{
    public AudioStateManager manager;

    [Header("Sélection (choix via asset = label)")]
    public AudioStateRefSO selected;

    [Tooltip("Auto apply quand 'selected' change (safe, pas de clamp d'index).")]
    public bool autoApplyOnChange = false;

    [Min(0f)]
    public float autoApplyDelay = 0.15f;

    private AudioStateRefSO _lastSelected;
    private float _applyAtTime = -1f;

    private void Reset()
    {
        manager = FindAnyObjectByType<AudioStateManager>();
    }

    private void Awake()
    {
        _lastSelected = selected;
    }

    private void Update()
    {
        if (!autoApplyOnChange) return;

        if (selected != _lastSelected)
        {
            _lastSelected = selected;
            _applyAtTime = Time.unscaledTime + autoApplyDelay;
        }

        if (_applyAtTime > 0f && Time.unscaledTime >= _applyAtTime)
        {
            _applyAtTime = -1f;
            Apply();
        }
    }

    public void Apply()
    {
        if (manager == null || selected == null) return;
        manager.Set(selected);
    }
}
