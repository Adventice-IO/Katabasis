using UnityEngine;
using Framework.Utils.Editor;

public class AutoSavePlay : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if(Application.isPlaying)
        {
            UnityPlayModeSaver.SaveComponent(transform);
        }

    }

void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
