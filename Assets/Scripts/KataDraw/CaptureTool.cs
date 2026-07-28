using System;
using System.Collections.Generic;
using System.IO;
using BAPointCloudRenderer.CloudController;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CaptureTool : MonoBehaviour
{
    private const string CaptureDirectoryName = "KataDrawCaptures";

    [Serializable]
    public sealed class RuntimeConfiguration
    {
        public float focalDistance = 10f;
        public float focalWidth = 10f;
        public float dotsThreshold;
        public bool blackAndWhite;
        public float fieldOfView = 60f;
        public string screenshotName = "Unnamed";
        public int printWidthMm;
        public int printHeightMm;
        public int pointBudget = 1000000;
    }

    public GameObject captureCamera;
    public KatabasisMeshConfiguration katabasisMeshConfig;

    [Header("Legacy UI")]
    public Slider focalDistanceSlider;
    public Slider focalWidthSlider;
    public TMP_InputField screenshotNameField;
    public Slider dotsThresholdSlider;
    public TMP_InputField printWidthField;
    public TMP_InputField printHeightField;
    public Slider fovSlider;
    public TMP_InputField budgetField;

    public DynamicPointCloudSet pointCloudSet;

    private RuntimeConfiguration _configuration;
    private readonly List<GameObject> _legacyCanvasObjects = new List<GameObject>();
    private readonly List<bool> _legacyCanvasStates = new List<bool>();
    private bool _managedBySettingsMenu;
    private bool _captureModeActive;
    private bool _hasPreviousMainCameraFov;
    private float _previousMainCameraFov;

    public string CaptureOutputDirectory
    {
        get
        {
#if UNITY_EDITOR
            var projectRoot = Directory.GetParent(Application.dataPath);
            var baseDirectory = projectRoot != null
                ? projectRoot.FullName
                : Application.dataPath;
#else
            var baseDirectory = Application.dataPath;
#endif
            return Path.Combine(baseDirectory, CaptureDirectoryName);
        }
    }

    private void OnEnable()
    {
        ResolveDependencies();
        EnsureConfiguration();

        if (_captureModeActive || !_managedBySettingsMenu)
        {
            BeginCaptureEffects();
        }
    }

    private void OnDisable()
    {
        EndCaptureEffects();
    }

    private void Start()
    {
        if (focalDistanceSlider != null)
        {
            focalDistanceSlider.onValueChanged.AddListener(setFocalDistance);
        }

        if (focalWidthSlider != null)
        {
            focalWidthSlider.onValueChanged.AddListener(setFocalWidth);
        }

        if (dotsThresholdSlider != null)
        {
            dotsThresholdSlider.onValueChanged.AddListener(setDotsThreshold);
        }

        if (fovSlider != null)
        {
            fovSlider.onValueChanged.AddListener(setFov);
        }
    }

    public void SetManagedBySettingsMenu(bool managed)
    {
        if (_managedBySettingsMenu == managed)
        {
            if (managed)
            {
                SetLegacyUIVisible(false);
            }

            return;
        }

        _managedBySettingsMenu = managed;
        if (managed)
        {
            CacheLegacyCanvasStates();
            SetLegacyUIVisible(false);
        }
        else
        {
            RestoreLegacyCanvasStates();
        }
    }

    public void SetCaptureModeActive(bool active)
    {
        _captureModeActive = active;
        if (_managedBySettingsMenu)
        {
            SetLegacyUIVisible(false);
        }

        if (active)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            else
            {
                BeginCaptureEffects();
            }
        }
        else
        {
            EndCaptureEffects();
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }

    public RuntimeConfiguration CaptureConfiguration()
    {
        EnsureConfiguration();
        return CopyConfiguration(_configuration);
    }

    public void ApplyConfiguration(RuntimeConfiguration configuration, bool applyPointBudget)
    {
        if (configuration == null)
        {
            return;
        }

        _configuration = NormalizeConfiguration(configuration);
        RefreshLegacyControls();

        if (isActiveAndEnabled && (_captureModeActive || !_managedBySettingsMenu))
        {
            ApplyCaptureEffects();
        }

        if (applyPointBudget)
        {
            ApplyPointBudget(_configuration.pointBudget, out _);
        }
    }

    public bool ApplyPointBudget(int requestedBudget, out string message)
    {
        ResolveDependencies();
        var normalizedBudget = Mathf.Max(1, requestedBudget);
        EnsureConfiguration();
        _configuration.pointBudget = normalizedBudget;

        if (pointCloudSet == null)
        {
            message = "No dynamic point-cloud set is available.";
            return false;
        }

        var renderer = pointCloudSet.PointRenderer;
        renderer?.Hide();
        pointCloudSet.pointBudget = (uint)normalizedBudget;
        renderer?.Display();

        message = $"Point budget set to {normalizedBudget:N0}.";
        return true;
    }

    public bool TryCaptureFrame(
        string screenshotName,
        int printWidthMm,
        int printHeightMm,
        out string savedPath,
        out string message)
    {
        savedPath = string.Empty;
        ResolveDependencies();
        EnsureConfiguration();

        var mainCamera = Camera.main;
        var cameraComponent = captureCamera != null
            ? captureCamera.GetComponent<Camera>()
            : null;
        if (mainCamera == null || cameraComponent == null)
        {
            message = "Capture requires both the main camera and the CaptureCamera.";
            return false;
        }

        var printWidth = printWidthMm > 0 ? printWidthMm * 2 : Screen.width;
        var printHeight = printHeightMm > 0 ? printHeightMm * 2 : Screen.height;
        if (printWidth <= 0 || printHeight <= 0)
        {
            message = "Capture dimensions must be greater than zero.";
            return false;
        }

        if (printWidth > Screen.width || printHeight > Screen.height)
        {
            message =
                $"The requested {printWidth} x {printHeight}px capture exceeds "
                + $"the current {Screen.width} x {Screen.height}px resolution.";
            return false;
        }

        _configuration.screenshotName = NormalizeScreenshotName(screenshotName);
        _configuration.printWidthMm = Mathf.Max(0, printWidthMm);
        _configuration.printHeightMm = Mathf.Max(0, printHeightMm);

        captureCamera.transform.SetPositionAndRotation(
            mainCamera.transform.position,
            mainCamera.transform.rotation);

        var wasCaptureCameraActive = captureCamera.activeSelf;
        var previousTarget = cameraComponent.targetTexture;
        var previousActiveTexture = RenderTexture.active;
        RenderTexture renderTexture = null;
        Texture2D capturedImage = null;

        try
        {
            renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
            captureCamera.SetActive(true);
            cameraComponent.targetTexture = renderTexture;
            cameraComponent.Render();

            capturedImage = new Texture2D(printWidth, printHeight, TextureFormat.RGB24, false);
            RenderTexture.active = renderTexture;
            var widthMargin = (Screen.width - printWidth) / 2f;
            var heightMargin = (Screen.height - printHeight) / 2f;
            capturedImage.ReadPixels(
                new Rect(widthMargin, heightMargin, printWidth, printHeight),
                0,
                0);
            capturedImage.Apply();

            Directory.CreateDirectory(CaptureOutputDirectory);
            savedPath = Path.Combine(
                CaptureOutputDirectory,
                _configuration.screenshotName + ".png");
            File.WriteAllBytes(savedPath, capturedImage.EncodeToPNG());
            message = "Captured frame saved to: " + savedPath;
            Debug.Log(message, this);
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not save the captured frame: " + exception.Message;
            Debug.LogError(message, this);
            return false;
        }
        finally
        {
            cameraComponent.targetTexture = previousTarget;
            RenderTexture.active = previousActiveTexture;
            captureCamera.SetActive(wasCaptureCameraActive);

            if (renderTexture != null)
            {
                Destroy(renderTexture);
            }

            if (capturedImage != null)
            {
                Destroy(capturedImage);
            }
        }
    }

    // Legacy uGUI callbacks retained for prefab compatibility.
    public void captureFrame()
    {
        TryCaptureFrame(
            screenshotNameField != null ? screenshotNameField.text : _configuration?.screenshotName,
            ParseNonNegativeInt(printWidthField),
            ParseNonNegativeInt(printHeightField),
            out _,
            out _);
    }

    public void setFocalDistance(float focalDistance)
    {
        EnsureConfiguration();
        _configuration.focalDistance = Mathf.Max(0f, focalDistance);
        SetMaterialFloat("_Focal", _configuration.focalDistance);
    }

    public void setFocalWidth(float focalWidth)
    {
        EnsureConfiguration();
        _configuration.focalWidth = Mathf.Max(0.0001f, focalWidth);
        SetMaterialFloat("_Focalwidth", _configuration.focalWidth);
    }

    public void setDotsThreshold(float threshold)
    {
        EnsureConfiguration();
        _configuration.dotsThreshold = Mathf.Clamp01(threshold);
        SetMaterialFloat(
            "_BlackAndWhiteThreshold",
            Mathf.Pow(_configuration.dotsThreshold, 5f));
    }

    public void setBlackAndWhiteMode(bool enable)
    {
        EnsureConfiguration();
        _configuration.blackAndWhite = enable;
        SetMaterialFloat("_EnableBlackAndWhite", enable ? 1f : 0f);
    }

    public void setFov(float fov)
    {
        EnsureConfiguration();
        _configuration.fieldOfView = Mathf.Clamp(fov, 1f, 179f);

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.fieldOfView = _configuration.fieldOfView;
        }

        var cameraComponent = captureCamera != null
            ? captureCamera.GetComponent<Camera>()
            : null;
        if (cameraComponent != null)
        {
            cameraComponent.fieldOfView = _configuration.fieldOfView;
        }
    }

    public void setPointBudget()
    {
        ApplyPointBudget(ParseNonNegativeInt(budgetField), out var message);
        Debug.Log(message, this);
    }

    private void ResolveDependencies()
    {
        if (katabasisMeshConfig == null)
        {
            katabasisMeshConfig =
                FindAnyObjectByType<KatabasisMeshConfiguration>(FindObjectsInactive.Include);
        }

        if (pointCloudSet == null)
        {
            pointCloudSet =
                FindAnyObjectByType<DynamicPointCloudSet>(FindObjectsInactive.Include);
        }

        if (captureCamera == null)
        {
            var childCamera = GetComponentInChildren<Camera>(true);
            captureCamera = childCamera != null ? childCamera.gameObject : null;
        }
    }

    private void EnsureConfiguration()
    {
        if (_configuration != null)
        {
            return;
        }

        ResolveDependencies();
        var material = katabasisMeshConfig != null
            ? katabasisMeshConfig.material
            : null;
        var threshold = material != null && material.HasProperty("_BlackAndWhiteThreshold")
            ? Mathf.Clamp01(material.GetFloat("_BlackAndWhiteThreshold"))
            : 0f;
        var mainCamera = Camera.main;

        _configuration = NormalizeConfiguration(new RuntimeConfiguration
        {
            focalDistance = GetMaterialFloat(
                material,
                "_Focal",
                focalDistanceSlider != null ? focalDistanceSlider.value : 10f),
            focalWidth = GetMaterialFloat(
                material,
                "_Focalwidth",
                focalWidthSlider != null ? focalWidthSlider.value : 10f),
            dotsThreshold = Mathf.Pow(threshold, 0.2f),
            blackAndWhite = material != null
                && material.HasProperty("_EnableBlackAndWhite")
                && material.GetFloat("_EnableBlackAndWhite") > 0.5f,
            fieldOfView = mainCamera != null
                ? mainCamera.fieldOfView
                : fovSlider != null
                    ? fovSlider.value
                    : 60f,
            screenshotName = screenshotNameField != null
                ? screenshotNameField.text
                : "Unnamed",
            printWidthMm = ParseNonNegativeInt(printWidthField),
            printHeightMm = ParseNonNegativeInt(printHeightField),
            pointBudget = pointCloudSet != null
                ? (int)Math.Min(pointCloudSet.pointBudget, int.MaxValue)
                : Mathf.Max(1, ParseNonNegativeInt(budgetField))
        });
    }

    private void BeginCaptureEffects()
    {
        if (!_hasPreviousMainCameraFov)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _previousMainCameraFov = mainCamera.fieldOfView;
                _hasPreviousMainCameraFov = true;
            }
        }

        ApplyCaptureEffects();
    }

    private void ApplyCaptureEffects()
    {
        EnsureConfiguration();
        SetMaterialFloat("_EnableFocalMode", 1f);
        setFocalDistance(_configuration.focalDistance);
        setFocalWidth(_configuration.focalWidth);
        setDotsThreshold(_configuration.dotsThreshold);
        setBlackAndWhiteMode(_configuration.blackAndWhite);
        setFov(_configuration.fieldOfView);
    }

    private void EndCaptureEffects()
    {
        ResolveDependencies();
        SetMaterialFloat("_EnableFocalMode", 0f);
        SetMaterialFloat("_EnableBlackAndWhite", 0f);

        if (_hasPreviousMainCameraFov)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.fieldOfView = _previousMainCameraFov;
            }

            _hasPreviousMainCameraFov = false;
        }
    }

    private void SetMaterialFloat(string propertyName, float value)
    {
        ResolveDependencies();
        var material = katabasisMeshConfig != null
            ? katabasisMeshConfig.material
            : null;
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private void CacheLegacyCanvasStates()
    {
        _legacyCanvasObjects.Clear();
        _legacyCanvasStates.Clear();
        var canvases = GetComponentsInChildren<Canvas>(true);
        for (var index = 0; index < canvases.Length; index++)
        {
            var canvasObject = canvases[index].gameObject;
            _legacyCanvasObjects.Add(canvasObject);
            _legacyCanvasStates.Add(canvasObject.activeSelf);
        }
    }

    private void SetLegacyUIVisible(bool visible)
    {
        if (_legacyCanvasObjects.Count == 0)
        {
            CacheLegacyCanvasStates();
        }

        for (var index = 0; index < _legacyCanvasObjects.Count; index++)
        {
            var canvasObject = _legacyCanvasObjects[index];
            if (canvasObject != null)
            {
                canvasObject.SetActive(visible);
            }
        }
    }

    private void RestoreLegacyCanvasStates()
    {
        for (var index = 0; index < _legacyCanvasObjects.Count; index++)
        {
            var canvasObject = _legacyCanvasObjects[index];
            if (canvasObject != null)
            {
                canvasObject.SetActive(_legacyCanvasStates[index]);
            }
        }

        _legacyCanvasObjects.Clear();
        _legacyCanvasStates.Clear();
    }

    private void RefreshLegacyControls()
    {
        if (focalDistanceSlider != null)
        {
            focalDistanceSlider.SetValueWithoutNotify(_configuration.focalDistance);
        }

        if (focalWidthSlider != null)
        {
            focalWidthSlider.SetValueWithoutNotify(_configuration.focalWidth);
        }

        if (dotsThresholdSlider != null)
        {
            dotsThresholdSlider.SetValueWithoutNotify(_configuration.dotsThreshold);
        }

        if (fovSlider != null)
        {
            fovSlider.SetValueWithoutNotify(_configuration.fieldOfView);
        }

        if (screenshotNameField != null)
        {
            screenshotNameField.SetTextWithoutNotify(_configuration.screenshotName);
        }

        if (printWidthField != null)
        {
            printWidthField.SetTextWithoutNotify(
                _configuration.printWidthMm > 0
                    ? _configuration.printWidthMm.ToString()
                    : string.Empty);
        }

        if (printHeightField != null)
        {
            printHeightField.SetTextWithoutNotify(
                _configuration.printHeightMm > 0
                    ? _configuration.printHeightMm.ToString()
                    : string.Empty);
        }

        if (budgetField != null)
        {
            budgetField.SetTextWithoutNotify(_configuration.pointBudget.ToString());
        }
    }

    private static RuntimeConfiguration NormalizeConfiguration(RuntimeConfiguration configuration)
    {
        return new RuntimeConfiguration
        {
            focalDistance = Mathf.Max(0f, Finite(configuration.focalDistance, 10f)),
            focalWidth = Mathf.Max(0.0001f, Finite(configuration.focalWidth, 10f)),
            dotsThreshold = Mathf.Clamp01(Finite(configuration.dotsThreshold, 0f)),
            blackAndWhite = configuration.blackAndWhite,
            fieldOfView = Mathf.Clamp(Finite(configuration.fieldOfView, 60f), 1f, 179f),
            screenshotName = NormalizeScreenshotName(configuration.screenshotName),
            printWidthMm = Mathf.Max(0, configuration.printWidthMm),
            printHeightMm = Mathf.Max(0, configuration.printHeightMm),
            pointBudget = Mathf.Max(1, configuration.pointBudget)
        };
    }

    private static RuntimeConfiguration CopyConfiguration(RuntimeConfiguration configuration)
    {
        return NormalizeConfiguration(configuration);
    }

    private static string NormalizeScreenshotName(string screenshotName)
    {
        var normalized = string.IsNullOrWhiteSpace(screenshotName)
            ? "Unnamed"
            : screenshotName.Trim();
        if (string.Equals(
                Path.GetExtension(normalized),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = Path.GetFileNameWithoutExtension(normalized);
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(normalized) ? "Unnamed" : normalized;
    }

    private static int ParseNonNegativeInt(TMP_InputField field)
    {
        return field != null && int.TryParse(field.text, out var value)
            ? Mathf.Max(0, value)
            : 0;
    }

    private static float GetMaterialFloat(
        Material material,
        string propertyName,
        float fallback)
    {
        return material != null && material.HasProperty(propertyName)
            ? material.GetFloat(propertyName)
            : fallback;
    }

    private static float Finite(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : value;
    }
}
