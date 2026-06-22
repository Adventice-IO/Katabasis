using UnityEngine;
using UnityEngine.InputSystem;

public class CaptureTool : MonoBehaviour
{
    public GameObject captureCamera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void captureFrame()
    {
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

        // Save the captured image as a PNG file
        byte[] bytes = capturedImage.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/CapturedFrame.png", bytes);

        Debug.Log("Captured frame saved to: " + Application.dataPath + "/CapturedFrame.png");
    }

}
