using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class OrbController : MonoBehaviour
{
    [Header("Mouse Control")]
    [Tooltip("Pan degrees added for each horizontal mouse pixel.")]
    [Min(0f)]
    [SerializeField] private float panSensitivity = 0.15f;
    [Tooltip("Tilt degrees added for each vertical mouse pixel.")]
    [Min(0f)]
    [SerializeField] private float tiltSensitivity = 0.15f;
    [Tooltip("Seconds used to smooth pan changes. Set to zero for an immediate response.")]
    [Min(0f)]
    [SerializeField] private float panSmoothing = 0.08f;
    [Tooltip("Seconds used to smooth tilt changes. Set to zero for an immediate response.")]
    [Min(0f)]
    [SerializeField] private float tiltSmoothing = 0.08f;
    [Tooltip("When enabled, mouse motion only changes the view while the right mouse button is held.")]
    [SerializeField] private bool requireRightMouseButton;
    [SerializeField] private bool invertPan;
    [SerializeField] private bool invertTilt;
    [SerializeField] private bool lockPan;
    [SerializeField] private bool lockTilt;

    [Header("View Reset")]
    [Tooltip("Return pan and tilt to zero after this many seconds without accepted mouse motion. Set to zero to disable the automatic reset.")]
    [Min(0f)]
    [SerializeField] private float viewResetTimeout = 10f;

    [Header("Tilt Limits")]
    [SerializeField] private float minimumTilt = -85f;
    [SerializeField] private float maximumTilt = 85f;

    private Quaternion _zeroRotation;
    private float _targetPan;
    private float _targetTilt;
    private float _currentPan;
    private float _currentTilt;
    private float _panVelocity;
    private float _tiltVelocity;
    private float _lastManipulationTime;
    private bool _initialized;

    public float PanSensitivity
    {
        get => panSensitivity;
        set => panSensitivity = Mathf.Max(0f, value);
    }

    public float TiltSensitivity
    {
        get => tiltSensitivity;
        set => tiltSensitivity = Mathf.Max(0f, value);
    }

    public float PanSmoothing
    {
        get => panSmoothing;
        set => panSmoothing = Mathf.Max(0f, value);
    }

    public float TiltSmoothing
    {
        get => tiltSmoothing;
        set => tiltSmoothing = Mathf.Max(0f, value);
    }

    public bool LockPan
    {
        get => lockPan;
        set => lockPan = value;
    }

    public bool RequireRightMouseButton
    {
        get => requireRightMouseButton;
        set => requireRightMouseButton = value;
    }

    public bool InvertPan
    {
        get => invertPan;
        set => invertPan = value;
    }

    public bool InvertTilt
    {
        get => invertTilt;
        set => invertTilt = value;
    }

    public bool LockTilt
    {
        get => lockTilt;
        set => lockTilt = value;
    }

    public float ViewResetTimeout
    {
        get => viewResetTimeout;
        set => viewResetTimeout = Mathf.Max(0f, value);
    }

    public float Pan => _currentPan;
    public float Tilt => _currentTilt;
    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        _lastManipulationTime = Time.unscaledTime;
    }

    private void OnValidate()
    {
        panSensitivity = Mathf.Max(0f, panSensitivity);
        tiltSensitivity = Mathf.Max(0f, tiltSensitivity);
        panSmoothing = Mathf.Max(0f, panSmoothing);
        tiltSmoothing = Mathf.Max(0f, tiltSmoothing);
        viewResetTimeout = Mathf.Max(0f, viewResetTimeout);

        if (minimumTilt > maximumTilt)
        {
            (minimumTilt, maximumTilt) = (maximumTilt, minimumTilt);
        }
    }

    private void LateUpdate()
    {
        ReadMouseInput();
        UpdateIdleReset();
        UpdateRotation();
    }

    public void ResetView(bool immediate = false)
    {
        _targetPan = 0f;
        _targetTilt = 0f;
        _panVelocity = 0f;
        _tiltVelocity = 0f;
        _lastManipulationTime = Time.unscaledTime;

        if (!immediate)
        {
            return;
        }

        _currentPan = 0f;
        _currentTilt = 0f;
        transform.localRotation = _zeroRotation;
    }

    private void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _zeroRotation = transform.localRotation;
        _lastManipulationTime = Time.unscaledTime;
        _initialized = true;
    }

    private void ReadMouseInput()
    {
        var mouse = Mouse.current;
        if (mouse == null
            || (requireRightMouseButton && !mouse.rightButton.isPressed))
        {
            return;
        }

        var delta = mouse.delta.ReadValue();
        if (Mathf.Approximately(delta.sqrMagnitude, 0f))
        {
            return;
        }

        if (!lockPan && !Mathf.Approximately(delta.x, 0f))
        {
            var panDirection = invertPan ? -1f : 1f;
            _targetPan = NormalizeAngle(_targetPan + delta.x * panSensitivity * panDirection);
        }

        if (!lockTilt && !Mathf.Approximately(delta.y, 0f))
        {
            var tiltDirection = invertTilt ? -1f : 1f;
            _targetTilt = Mathf.Clamp(
                _targetTilt - delta.y * tiltSensitivity * tiltDirection,
                minimumTilt,
                maximumTilt);
        }

        _lastManipulationTime = Time.unscaledTime;
    }

    private void UpdateIdleReset()
    {
        if (viewResetTimeout <= 0f
            || Time.unscaledTime - _lastManipulationTime < viewResetTimeout)
        {
            return;
        }

        _targetPan = 0f;
        _targetTilt = 0f;
    }

    private void UpdateRotation()
    {
        var deltaTime = Time.unscaledDeltaTime;

        _currentPan = panSmoothing <= 0f
            ? _targetPan
            : Mathf.SmoothDampAngle(
                _currentPan,
                _targetPan,
                ref _panVelocity,
                panSmoothing,
                Mathf.Infinity,
                deltaTime);

        _currentTilt = tiltSmoothing <= 0f
            ? _targetTilt
            : Mathf.SmoothDamp(
                _currentTilt,
                _targetTilt,
                ref _tiltVelocity,
                tiltSmoothing,
                Mathf.Infinity,
                deltaTime);

        transform.localRotation = _zeroRotation * Quaternion.Euler(_currentTilt, _currentPan, 0f);
    }

    private static float NormalizeAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }
}
