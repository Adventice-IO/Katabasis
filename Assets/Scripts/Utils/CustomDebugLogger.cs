using System;
using UnityEngine;

[ExecuteInEditMode]
public class CustomDebugLogger : MonoBehaviour, ILogHandler
{
    public bool logMetaXRFeature = false;
    public bool logWwise = false;
    private ILogHandler defaultLogger = Debug.unityLogger.logHandler;

    private void OnEnable()
    {
        Debug.unityLogger.logHandler = this;
    }

    void ILogHandler.LogException(Exception exception, UnityEngine.Object context)
    {
        // native call
        defaultLogger.LogException(exception, context); //this cause recursive call

    }

    void ILogHandler.LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        bool isMetaXRLog = format.Contains("[MetaXRFeature]");
        if (isMetaXRLog && !logMetaXRFeature)
        {
            return; // Skip logging MetaXR messages if the flag is false
        }

        bool isWwiseLog = format.Contains("WwiseUnity");
        if (isWwiseLog && !logWwise)
        {
            return; // Skip logging Wwise messages if the flag is false
        }
        defaultLogger.LogFormat(logType, context, format, args);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
