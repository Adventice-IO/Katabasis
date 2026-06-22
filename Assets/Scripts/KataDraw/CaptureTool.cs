using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class CaptureTool : MonoBehaviour
{
    public GameObject captureCamera;

    public KatabasisMeshConfiguration katabasisMeshConfig;

    public Slider focalDistanceSlider;
    public Slider focalWidthSlider;
    public TMP_InputField screenshotNameField;


    //enabling and disabling the focal mode when the capture tool is enabled or disabled

    void OnEnable()
    {
            if (katabasisMeshConfig != null && katabasisMeshConfig.material != null)
            {
                katabasisMeshConfig.material.SetFloat("_EnableFocalMode", 1);
            }
    }

    void OnDisable()
    {
            if (katabasisMeshConfig != null && katabasisMeshConfig.material != null)
            {
                katabasisMeshConfig.material.SetFloat("_EnableFocalMode", 0);
            }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        focalDistanceSlider.onValueChanged.AddListener(delegate { setFocalDistance(focalDistanceSlider.value); });
        focalWidthSlider.onValueChanged.AddListener(delegate { setFocalWidth(focalWidthSlider.value); });
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void captureFrame()
    {
        //synchronize the capturecamera's position and ortation with the main camera
        captureCamera.transform.position = Camera.main.transform.position;
        captureCamera.transform.rotation = Camera.main.transform.rotation;

        // Create a RenderTexture to capture the camera's output
        RenderTexture renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        captureCamera.SetActive(true);
        Camera cameraComponent = captureCamera.GetComponent<Camera>();

        cameraComponent.targetTexture = renderTexture;
        cameraComponent.Render();

        // Read the pixels from the RenderTexture into a Texture2D
        Texture2D capturedImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        capturedImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        capturedImage.Apply();

        // Clean up
        cameraComponent.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        captureCamera.SetActive(false);

        //get the screenshot name from the input field
        string screenshotName = screenshotNameField.text;

        // Save the captured image as a PNG file
        byte[] bytes = capturedImage.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/KataDrawCaptures/" + screenshotName + ".png", bytes);

        Debug.Log("Captured frame saved to: " + Application.dataPath + "/KataDrawCaptures/" + screenshotName + ".png");
    }

    public void setFocalDistance(float focalDistance)
    {
        if (katabasisMeshConfig != null && katabasisMeshConfig.material != null)
        {
            katabasisMeshConfig.material.SetFloat("_Focal", focalDistance);
        }
    }

    public void setFocalWidth(float focalWidth)
    {
        if (katabasisMeshConfig != null && katabasisMeshConfig.material != null)
        {
            katabasisMeshConfig.material.SetFloat("_Focalwidth", focalWidth);
        }
    }

}
