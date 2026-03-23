using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameOutro : MonoBehaviour
{
    bool isActive = false;
    float timeOnActiveChange = 0f;

    public float totalRevealTime = 4f;
    public float cartonRevealTime = 1f;
    public float firstCartonExtraTime = 1f;
    public float finishFadeOutTime = 1f;
    public float layoutRadius = 2f;
    public float angleStep = 20f;
    public bool autoAngle = true;
    public bool autoNext = true;
    public float cartonScale = 1f;
    public float nextDelay = 2f;
    public bool fadePointCloud = false;
    public float pointCloudFadeTime = 2f;

    public List<Texture2D> cartons = new List<Texture2D>();
    public List<GameObject> cartonObjects = new List<GameObject>();

    bool outroFinished = false;
    bool waitingForCartons;

    MainController mainController;
    DataManager dataManager;


    void Start()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
        LoadCartons();
    }

    void Update()
    {
        UpdateCartonLayout();

        if (mainController == null)
        {
            mainController = GameObject.FindAnyObjectByType<MainController>();
        }

        if (!isActive)
        {
            return;
        }

        float timeSinceActiveChange = Time.time - timeOnActiveChange;

        if (cartons == null || cartons.Count == 0)
        {
            FinishOutro();
            return;
        }

        UpdateCartonReveal(timeSinceActiveChange);
        UpdatePointCloudFade(timeSinceActiveChange);

        float totalOutroDuration = totalRevealTime + finishFadeOutTime + nextDelay;
        if (timeSinceActiveChange >= totalOutroDuration)
        {
            FinishOutro();
        }
    }

    public void setActive(bool active)
    {
        isActive = active;
        timeOnActiveChange = Time.time;

        if (active)
        {
            LoadCartons();
            outroFinished = false;
            if (mainController != null)
            {
            }
        }
        else
        {
            outroFinished = false;
            HideCurrentCartons();
            if (mainController != null && !fadePointCloud)
            {
                mainController.pointCloudViewDistanceMultiplier = 1f;
            }
        }
    }

    void LoadCartons()
    {
        if (cartons != null)
        {
            foreach (Texture2D texture in cartons)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        cartons.Clear();
        HideCurrentCartons();

        string outroPath = dataManager.GetBasePath(DataManager.DataFolder.Outro);
        if (!Directory.Exists(outroPath))
        {
            Debug.LogWarning("Outro directory not found: " + outroPath);
            return;
        }

        string[] pngFiles = Directory.GetFiles(outroPath, "*.png")
            .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Debug.Log("Found " + pngFiles.Length + " outro carton(s) in: " + outroPath);

        foreach (string pngFile in pngFiles)
        {
            byte[] pngData = File.ReadAllBytes(pngFile);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(pngData))
            {
                texture.name = Path.GetFileNameWithoutExtension(pngFile);
                cartons.Add(texture);
            }
            else
            {
                Destroy(texture);
            }
        }

        CreateCartonObjects();
    }

    void OnValidate()
    {
        UpdateCartonLayout();
    }

    void CreateCartonObjects()
    {
        HideCurrentCartons();

        Debug.Log("Creating carton objects for " + cartons.Count + " carton(s).");
        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            return;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward == Vector3.zero)
        {
            forward = targetCamera.transform.forward;
            forward.y = 0f;
            if (forward == Vector3.zero)
            {
                forward = Vector3.forward;
            }
        }

        forward.Normalize();
        for (int i = 0; i < cartons.Count; i++)
        {
            Texture2D texture = cartons[i];
            if (texture == null)
            {
                Debug.Log("Skipping null texture for carton index " + i);
                continue;
            }

            GameObject cartonObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cartonObject.name = "OutroCarton_" + i;
            Destroy(cartonObject.GetComponent<Collider>());
            cartonObject.transform.SetParent(transform, false);

            cartonObject.transform.localScale = new Vector3(cartonScale, cartonScale, 1f);

            MeshRenderer cartonRenderer = cartonObject.GetComponent<MeshRenderer>();
            Shader unlitTransparent = Shader.Find("Sprites/Default");
            if (unlitTransparent == null)
            {
                unlitTransparent = Shader.Find("Unlit/Texture");
            }

            Material cartonMaterial = new Material(unlitTransparent);
            cartonMaterial.mainTexture = texture;
            cartonRenderer.material = cartonMaterial;
            SetCartonAlpha(cartonRenderer, 0f);

            cartonObjects.Add(cartonObject);
            Debug.Log("Created carton object for texture: " + texture.name);
        }

        UpdateCartonLayout();
    }

    float GetCartonAngle(int index)
    {
        if (cartons == null || cartons.Count == 0)
        {
            return 0f;
        }

        float step = GetResolvedAngleStep();
        int count = cartons.Count;

        if (count % 2 == 1)
        {
            int centeredIndex = index - (count / 2);
            return centeredIndex * step;
        }

        float centeredOffset = index - ((count - 1) * 0.5f);
        return centeredOffset * step;
    }

    float GetResolvedAngleStep()
    {
        if (!autoAngle)
        {
            return angleStep;
        }

        int sideCount = cartons != null && cartons.Count > 1 ? ((cartons.Count - 1) + 1) / 2 : 0;
        if (sideCount <= 0)
        {
            return angleStep;
        }

        return 180f / (sideCount + 1);
    }

    void UpdateCartonLayout()
    {
        if (cartonObjects == null || cartonObjects.Count == 0)
        {
            return;
        }

        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            return;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward == Vector3.zero)
        {
            forward = targetCamera.transform.forward;
            forward.y = 0f;
            if (forward == Vector3.zero)
            {
                forward = Vector3.forward;
            }
        }

        forward.Normalize();

        for (int i = 0; i < cartonObjects.Count; i++)
        {
            GameObject cartonObject = cartonObjects[i];
            if (cartonObject == null)
            {
                continue;
            }

            Texture2D texture = i < cartons.Count ? cartons[i] : null;
            cartonObject.transform.localScale = new Vector3(cartonScale, cartonScale, 1f);

            float angle = GetCartonAngle(i);
            Quaternion rotationOffset = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 direction = rotationOffset * forward;
            cartonObject.transform.position = transform.position + (direction * layoutRadius);
            cartonObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    float GetCartonStepDelay()
    {
        if (cartons == null || cartons.Count <= 1)
        {
            return 0f;
        }

        float remainingTime = Mathf.Max(0f, totalRevealTime - firstCartonExtraTime - cartonRevealTime);
        return remainingTime <= 0f ? 0f : remainingTime / (cartons.Count - 1);
    }

    float GetCartonStartTime(int index)
    {
        if (index <= 0)
        {
            return 0f;
        }

        return firstCartonExtraTime + (GetCartonStepDelay() * (index - 1));
    }

    float GetRevealEndTime()
    {
        return Mathf.Max(0f, totalRevealTime);
    }

    void UpdateCartonReveal(float elapsed)
    {
        float revealEndTime = GetRevealEndTime();
        float fadeOutStart = revealEndTime;

        for (int i = 0; i < cartonObjects.Count; i++)
        {
            GameObject cartonObject = cartonObjects[i];
            if (cartonObject == null)
            {
                continue;
            }

            MeshRenderer cartonRenderer = cartonObject.GetComponent<MeshRenderer>();
            if (cartonRenderer == null)
            {
                continue;
            }

            float cartonStart = GetCartonStartTime(i);
            float revealProgress = cartonRevealTime <= 0f ? (elapsed >= cartonStart ? 1f : 0f) : Mathf.Clamp01((elapsed - cartonStart) / cartonRevealTime);
            float fadeOutMultiplier = 1f;
            if (finishFadeOutTime <= 0f)
            {
                fadeOutMultiplier = elapsed >= fadeOutStart ? 0f : 1f;
            }
            else if (elapsed > fadeOutStart)
            {
                fadeOutMultiplier = 1f - Mathf.Clamp01((elapsed - fadeOutStart) / finishFadeOutTime);
            }

            SetCartonAlpha(cartonRenderer, revealProgress * fadeOutMultiplier);
        }
    }

    void UpdatePointCloudFade(float elapsed)
    {
        if (!fadePointCloud || mainController == null)
        {
            return;
        }

        float fadeDuration = pointCloudFadeTime <= 0f ? totalRevealTime : pointCloudFadeTime;
        float fadeStart = Mathf.Max(0f, totalRevealTime - fadeDuration);
        float progress = fadeDuration <= 0f ? 1f : Mathf.Clamp01((elapsed - fadeStart) / fadeDuration);
        mainController.pointCloudViewDistanceMultiplier = Mathf.Lerp(1f, 0f, progress);
    }

    void SetCartonAlpha(MeshRenderer renderer, float alpha)
    {
        if (renderer == null || renderer.material == null)
        {
            return;
        }

        Material material = renderer.material;
        if (!material.HasProperty("_Color"))
        {
            return;
        }

        Color color = material.color;
        color.a = alpha;
        material.color = color;
    }

    void HideCurrentCartons()
    {
        Debug.Log("Hiding and destroying " + cartonObjects.Count + " existing carton object(s).");
        for (int i = 0; i < cartonObjects.Count; i++)
        {
            GameObject cartonObject = cartonObjects[i];
            if (cartonObject == null)
            {
                continue;
            }

            MeshRenderer renderer = cartonObject.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                Destroy(renderer.material);
            }

            Destroy(cartonObject);
        }

        cartonObjects.Clear();
    }

    void FinishOutro()
    {
        if (outroFinished)
        {
            return;
        }

        outroFinished = true;
        isActive = false;
        if(autoNext) mainController.gameState = MainController.GameState.End;
    }
}
