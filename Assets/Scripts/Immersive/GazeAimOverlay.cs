using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
public sealed class GazeAimOverlay : MonoBehaviour
{
    private const string OverlayShaderName = "Katabasis/AimCircleOverlay";

    [SerializeField] private bool visible;
    [Min(4f)][SerializeField] private float sizePixels = 36f;
    [Min(.5f)][SerializeField] private float thicknessPixels = 3f;
    [Range(0f, 1f)][SerializeField] private float opacity = .9f;
    [SerializeField] private Color color = Color.white;

    private TransformFollower _transformFollower;
    private ImmersiveController _immersiveController;
    private Camera _overlayCamera;
    private Camera _overlayBaseCamera;
    private MeshRenderer _circleRenderer;
    private Mesh _circleMesh;
    private Material _circleMaterial;
    private Camera _excludedMainCamera;
    private bool _mainCameraOriginallyRenderedLayer;
    private int _overlayLayer = -1;
    private bool _stackWarningLogged;
    private bool _layerWarningLogged;
    private bool _shaderWarningLogged;
    private bool _hasCurrentSurface;
    private ImmersiveController.SurfaceId _currentSurface;

    public bool Visible => visible;
    public float SizePixels => sizePixels;
    public float ThicknessPixels => thicknessPixels;
    public float Opacity => opacity;
    public Color Color => color;
    public bool HasAimSource => _transformFollower != null && _transformFollower.target != null;
    public bool IsRendering => _hasCurrentSurface
        && _overlayBaseCamera != null
        && _overlayCamera != null
        && _overlayCamera.enabled
        && _circleRenderer != null
        && _circleRenderer.enabled;
    public ImmersiveController.SurfaceId CurrentSurface => _currentSurface;

    public void Initialize(
        TransformFollower transformFollower,
        ImmersiveController immersiveController)
    {
        _transformFollower = transformFollower;
        _immersiveController = immersiveController;
    }

    public void Configure(
        bool show,
        float diameterPixels,
        float ringThicknessPixels,
        float alpha,
        Color ringColor)
    {
        visible = show;
        sizePixels = Mathf.Clamp(SanitizeFloat(diameterPixels, 36f), 4f, 512f);
        thicknessPixels = Mathf.Clamp(
            SanitizeFloat(ringThicknessPixels, 3f),
            .5f,
            sizePixels * .5f);
        opacity = Mathf.Clamp01(SanitizeFloat(alpha, .9f));
        color = SanitizeColor(ringColor);

        UpdateMaterial();
        if (!visible)
        {
            HideRuntimeOverlay();
        }
    }

    private void OnValidate()
    {
        sizePixels = Mathf.Clamp(SanitizeFloat(sizePixels, 36f), 4f, 512f);
        thicknessPixels = Mathf.Clamp(
            SanitizeFloat(thicknessPixels, 3f),
            .5f,
            sizePixels * .5f);
        opacity = Mathf.Clamp01(SanitizeFloat(opacity, .9f));
        color = SanitizeColor(color);
        UpdateMaterial();
    }

    private void LateUpdate()
    {
        if (!visible || !Application.isPlaying)
        {
            HideRuntimeOverlay();
            return;
        }

        ResolveDependencies();
        if (_immersiveController == null
            || !TryGetTheoreticalAimRay(out var aimOrigin, out var aimDirection))
        {
            HideRuntimeOverlay();
            return;
        }

        _overlayLayer = _immersiveController.AimOverlayLayer;
        if (_overlayLayer < 0)
        {
            _overlayLayer = LayerMask.NameToLayer(ImmersiveController.AimOverlayLayerName);
        }

        if (_overlayLayer < 0)
        {
            if (!_layerWarningLogged)
            {
                Debug.LogError(
                    $"The '{ImmersiveController.AimOverlayLayerName}' layer is required for the gaze aim overlay.",
                    this);
                _layerWarningLogged = true;
            }

            HideRuntimeOverlay();
            return;
        }

        _layerWarningLogged = false;
        if (!TryFindAimSurface(
                aimOrigin,
                aimDirection,
                out var surface,
                out var surfaceCamera,
                out var viewportPosition)
            || !EnsureRuntimeObjects()
            || !AttachOverlayCamera(surfaceCamera))
        {
            HideRuntimeOverlay();
            return;
        }

        _currentSurface = surface;
        _hasCurrentSurface = true;
        ExcludeOverlayFromMainCamera();
        UpdateCirclePlacement(surfaceCamera, viewportPosition);
        UpdateMaterial();
        _overlayCamera.enabled = true;
        _circleRenderer.enabled = true;
    }

    private void ResolveDependencies()
    {
        if (_transformFollower == null)
        {
            var followers = FindObjectsByType<TransformFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < followers.Length; index++)
            {
                if (followers[index] != null && followers[index].GetComponent<XRRayInteractor>() != null)
                {
                    _transformFollower = followers[index];
                    break;
                }
            }

            if (_transformFollower == null && followers.Length > 0)
            {
                _transformFollower = followers[0];
            }
        }

        if (_immersiveController == null)
        {
            _immersiveController =
                FindAnyObjectByType<ImmersiveController>(FindObjectsInactive.Include);
        }

    }

    private bool TryGetTheoreticalAimRay(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (_transformFollower == null || _transformFollower.target == null)
        {
            return false;
        }

        var view = _transformFollower.target;
        origin = view.position;
        direction = view.rotation
            * Quaternion.Euler(_transformFollower.ActiveVerticalOffset, 0f, 0f)
            * Vector3.forward;
        return direction.sqrMagnitude > Mathf.Epsilon;
    }

    private bool TryFindAimSurface(
        Vector3 aimOrigin,
        Vector3 aimDirection,
        out ImmersiveController.SurfaceId surface,
        out Camera surfaceCamera,
        out Vector3 viewportPosition)
    {
        if (!_immersiveController.TryProjectWorldRayToOutput(
                aimOrigin,
                aimDirection,
                out surface,
                out surfaceCamera,
                out viewportPosition)
            || surfaceCamera == null
            || !surfaceCamera.isActiveAndEnabled)
        {
            return false;
        }

        viewportPosition.x = Mathf.Clamp01(viewportPosition.x);
        viewportPosition.y = Mathf.Clamp01(viewportPosition.y);
        return true;
    }

    private bool EnsureRuntimeObjects()
    {
        if (_overlayCamera != null && _circleRenderer != null && _circleMaterial != null)
        {
            ApplyOverlayLayer();
            return true;
        }

        var shader = Shader.Find(OverlayShaderName);
        if (shader == null)
        {
            if (!_shaderWarningLogged)
            {
                Debug.LogError($"Shader '{OverlayShaderName}' could not be found.", this);
                _shaderWarningLogged = true;
            }

            return false;
        }

        _shaderWarningLogged = false;
        var cameraObject = new GameObject("Gaze Aim 2D Overlay Camera")
        {
            hideFlags = HideFlags.HideInHierarchy,
            layer = _overlayLayer
        };

        _overlayCamera = cameraObject.AddComponent<Camera>();
        _overlayCamera.enabled = false;
        _overlayCamera.orthographic = true;
        _overlayCamera.orthographicSize = .5f;
        _overlayCamera.nearClipPlane = .01f;
        _overlayCamera.farClipPlane = 10f;
        _overlayCamera.clearFlags = CameraClearFlags.Nothing;
        _overlayCamera.cullingMask = 1 << _overlayLayer;
        _overlayCamera.useOcclusionCulling = false;
        _overlayCamera.allowHDR = false;
        _overlayCamera.allowMSAA = false;
        _overlayCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var overlayData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;
        overlayData.renderShadows = false;

        var circleObject = new GameObject("Aim Circle")
        {
            hideFlags = HideFlags.HideInHierarchy,
            layer = _overlayLayer
        };
        circleObject.transform.SetParent(_overlayCamera.transform, false);

        var meshFilter = circleObject.AddComponent<MeshFilter>();
        _circleMesh = CreateQuadMesh();
        meshFilter.sharedMesh = _circleMesh;
        _circleRenderer = circleObject.AddComponent<MeshRenderer>();
        _circleMaterial = new Material(shader)
        {
            name = "Gaze Aim Overlay Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        _circleRenderer.sharedMaterial = _circleMaterial;
        _circleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _circleRenderer.receiveShadows = false;
        _circleRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _circleRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _circleRenderer.enabled = false;
        UpdateMaterial();
        return true;
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "Gaze Aim Overlay Quad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-.5f, -.5f, 0f),
                new Vector3(-.5f, .5f, 0f),
                new Vector3(.5f, .5f, 0f),
                new Vector3(.5f, -.5f, 0f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ApplyOverlayLayer()
    {
        if (_overlayCamera == null)
        {
            return;
        }

        _overlayCamera.gameObject.layer = _overlayLayer;
        _overlayCamera.cullingMask = 1 << _overlayLayer;
        if (_circleRenderer != null)
        {
            _circleRenderer.gameObject.layer = _overlayLayer;
        }
    }

    private bool AttachOverlayCamera(Camera surfaceCamera)
    {
        if (_overlayCamera == null || surfaceCamera == null)
        {
            return false;
        }

        var surfaceData = surfaceCamera.GetUniversalAdditionalCameraData();
        var cameraStack = surfaceData.cameraStack;
        if (cameraStack == null)
        {
            if (!_stackWarningLogged)
            {
                Debug.LogError(
                    $"The renderer used by {surfaceCamera.name} does not support URP camera stacking; "
                    + "the gaze aim overlay cannot be composited into this immersive output.",
                    this);
                _stackWarningLogged = true;
            }

            return false;
        }

        _stackWarningLogged = false;
        if (_overlayBaseCamera == surfaceCamera && cameraStack.Contains(_overlayCamera))
        {
            return true;
        }

        DetachOverlayCamera();
        cameraStack.RemoveAll(camera => camera == null);
        cameraStack.Add(_overlayCamera);
        _overlayBaseCamera = surfaceCamera;
        return true;
    }

    private void DetachOverlayCamera()
    {
        if (_overlayBaseCamera != null && _overlayCamera != null)
        {
            var surfaceData = _overlayBaseCamera.GetComponent<UniversalAdditionalCameraData>();
            surfaceData?.cameraStack.Remove(_overlayCamera);
        }

        _overlayBaseCamera = null;
    }

    private void UpdateCirclePlacement(Camera surfaceCamera, Vector3 viewportPosition)
    {
        var targetTexture = surfaceCamera.targetTexture as RenderTexture;
        var width = targetTexture != null ? targetTexture.width : Mathf.Max(1, surfaceCamera.pixelWidth);
        var height = targetTexture != null ? targetTexture.height : Mathf.Max(1, surfaceCamera.pixelHeight);
        var aspect = width / Mathf.Max(1f, height);
        var diameterWorld = sizePixels / Mathf.Max(1f, height);

        _overlayCamera.aspect = aspect;
        _overlayCamera.orthographicSize = .5f;
        _circleRenderer.transform.localPosition = new Vector3(
            (viewportPosition.x - .5f) * aspect,
            viewportPosition.y - .5f,
            1f);
        _circleRenderer.transform.localRotation = Quaternion.identity;
        _circleRenderer.transform.localScale = new Vector3(diameterWorld, diameterWorld, 1f);
    }

    private void UpdateMaterial()
    {
        if (_circleMaterial == null)
        {
            return;
        }

        var materialColor = color;
        materialColor.a = opacity;
        _circleMaterial.SetColor("_BaseColor", materialColor);
        _circleMaterial.SetFloat("_Thickness", Mathf.Clamp(thicknessPixels / sizePixels, 0f, .5f));
    }

    private void ExcludeOverlayFromMainCamera()
    {
        var mainCamera = Camera.main;
        if (mainCamera == null || _overlayLayer < 0)
        {
            return;
        }

        if (_excludedMainCamera != mainCamera)
        {
            RestoreMainCameraLayer();
            _excludedMainCamera = mainCamera;
            _mainCameraOriginallyRenderedLayer = (mainCamera.cullingMask & (1 << _overlayLayer)) != 0;
        }

        mainCamera.cullingMask &= ~(1 << _overlayLayer);
    }

    private void RestoreMainCameraLayer()
    {
        if (_excludedMainCamera == null || _overlayLayer < 0)
        {
            _excludedMainCamera = null;
            return;
        }

        if (_mainCameraOriginallyRenderedLayer)
        {
            _excludedMainCamera.cullingMask |= 1 << _overlayLayer;
        }
        else
        {
            _excludedMainCamera.cullingMask &= ~(1 << _overlayLayer);
        }

        _excludedMainCamera = null;
    }

    private void HideRuntimeOverlay()
    {
        _hasCurrentSurface = false;
        if (_circleRenderer != null)
        {
            _circleRenderer.enabled = false;
        }

        if (_overlayCamera != null)
        {
            _overlayCamera.enabled = false;
        }

        DetachOverlayCamera();
        RestoreMainCameraLayer();
    }

    private void OnDisable()
    {
        HideRuntimeOverlay();
        DestroyRuntimeObjects();
    }

    private void DestroyRuntimeObjects()
    {
        if (_overlayCamera != null)
        {
            var cameraObject = _overlayCamera.gameObject;
            _overlayCamera = null;
            DestroyRuntimeObject(cameraObject);
        }

        _circleRenderer = null;
        if (_circleMaterial != null)
        {
            DestroyRuntimeObject(_circleMaterial);
            _circleMaterial = null;
        }

        if (_circleMesh != null)
        {
            DestroyRuntimeObject(_circleMesh);
            _circleMesh = null;
        }
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static float SanitizeFloat(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static Color SanitizeColor(Color value)
    {
        return new Color(
            Mathf.Clamp01(SanitizeFloat(value.r, 1f)),
            Mathf.Clamp01(SanitizeFloat(value.g, 1f)),
            Mathf.Clamp01(SanitizeFloat(value.b, 1f)),
            1f);
    }
}
