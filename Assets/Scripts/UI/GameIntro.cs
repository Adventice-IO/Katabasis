using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameIntro : MonoBehaviour
{
    bool isActive = false;
    float timeOnActiveChange = 0f;

    public float cartonTime = 2f;
    public float cartonFadeTime = 1f;
    public float spawnDistance = 2f;
    public Vector2 cartonSize = new Vector2(1.6f, 0.9f);

    public List<Texture2D> cartons = new List<Texture2D>();

    GameObject currentCartonObject;
    MeshRenderer currentCartonRenderer;
    Material currentCartonMaterial;
    int currentCartonIndex = -1;
    bool introFinished = false;
    bool waitingForCartons;

    void Start()
    {
        LoadCartons();
    }

    void Update()
    {
        if (!isActive)
        {
            return;
        }

        float timeSinceActiveChange = Time.time - timeOnActiveChange;
        float cartonDuration = cartonFadeTime + cartonTime + cartonFadeTime;

        if (cartons == null || cartons.Count == 0 || cartonDuration <= 0f)
        {
            FinishIntro();
            return;
        }

        int newCartonIndex = Mathf.FloorToInt(timeSinceActiveChange / cartonDuration);
        if (newCartonIndex >= cartons.Count)
        {
            HideCurrentCarton();
            FinishIntro();
            return;
        }

        if (newCartonIndex != currentCartonIndex)
        {
            ShowCarton(newCartonIndex);
        }

        float cartonElapsed = timeSinceActiveChange - (newCartonIndex * cartonDuration);
        UpdateCartonAlpha(cartonElapsed);
    }

    public void setActive(bool active)
    {
        isActive = active;
        timeOnActiveChange = Time.time;

        if (active)
        {
            LoadCartons();
            introFinished = false;
            currentCartonIndex = -1;
            HideCurrentCarton();
        }
        else
        {
            introFinished = false;
            currentCartonIndex = -1;
            HideCurrentCarton();
        }
    }

    void LoadCartons()
    {
        if (!DataManager.IsFolderReady(DataManager.DataFolder.Intro))
        {
            if (!waitingForCartons)
            {
                waitingForCartons = true;
                DataManager.PreloadFolder(DataManager.DataFolder.Intro, (success, path) =>
                {
                    waitingForCartons = false;
                    if (success)
                    {
                        LoadCartons();
                    }
                });
            }
            return;
        }

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

        string introPath = DataManager.GetBasePath(DataManager.DataFolder.Intro);
        if (!Directory.Exists(introPath))
        {
            return;
        }

        string[] pngFiles = Directory.GetFiles(introPath, "*.png")
            .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
    }

    void ShowCarton(int index)
    {
        HideCurrentCarton();

        Texture2D texture = cartons[index];
        if (texture == null)
        {
            currentCartonIndex = index;
            return;
        }

        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            currentCartonIndex = index;
            return;
        }

        currentCartonObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        currentCartonObject.name = "IntroCarton_" + index;
        Destroy(currentCartonObject.GetComponent<Collider>());

        Vector3 spawnPosition = targetCamera.transform.position + (targetCamera.transform.forward * spawnDistance);
        currentCartonObject.transform.position = spawnPosition;
        currentCartonObject.transform.rotation = Quaternion.LookRotation(currentCartonObject.transform.position - targetCamera.transform.position, Vector3.up);

        float width = cartonSize.x;
        float height = cartonSize.y;
        if (texture.height > 0)
        {
            height = width * ((float)texture.height / texture.width);
        }
        currentCartonObject.transform.localScale = new Vector3(width, height, 1f);

        currentCartonRenderer = currentCartonObject.GetComponent<MeshRenderer>();
        Shader unlitTransparent = Shader.Find("Sprites/Default");
        if (unlitTransparent == null)
        {
            unlitTransparent = Shader.Find("Unlit/Texture");
        }

        currentCartonMaterial = new Material(unlitTransparent);
        currentCartonMaterial.mainTexture = texture;
        currentCartonRenderer.material = currentCartonMaterial;

        currentCartonIndex = index;
        SetCurrentCartonAlpha(0f);
    }

    void UpdateCartonAlpha(float cartonElapsed)
    {
        if (currentCartonMaterial == null)
        {
            return;
        }

        float alpha;
        if (cartonFadeTime <= 0f)
        {
            alpha = cartonElapsed < cartonTime ? 1f : 0f;
        }
        else if (cartonElapsed < cartonFadeTime)
        {
            alpha = Mathf.Clamp01(cartonElapsed / cartonFadeTime);
        }
        else if (cartonElapsed < cartonFadeTime + cartonTime)
        {
            alpha = 1f;
        }
        else
        {
            float fadeOutElapsed = cartonElapsed - cartonFadeTime - cartonTime;
            alpha = 1f - Mathf.Clamp01(fadeOutElapsed / cartonFadeTime);
        }

        SetCurrentCartonAlpha(alpha);
    }

    void SetCurrentCartonAlpha(float alpha)
    {
        if (currentCartonMaterial == null)
        {
            return;
        }

        if (!currentCartonMaterial.HasProperty("_Color"))
        {
            return;
        }

        Color color = currentCartonMaterial.color;
        color.a = alpha;
        currentCartonMaterial.color = color;
    }

    void HideCurrentCarton()
    {
        if (currentCartonObject != null)
        {
            Destroy(currentCartonObject);
        }

        if (currentCartonMaterial != null)
        {
            Destroy(currentCartonMaterial);
        }

        currentCartonObject = null;
        currentCartonRenderer = null;
        currentCartonMaterial = null;
    }

    void FinishIntro()
    {
        if (introFinished)
        {
            return;
        }

        introFinished = true;
        isActive = false;
        MainController.instance.gameState = MainController.GameState.Playing;
    }
}
