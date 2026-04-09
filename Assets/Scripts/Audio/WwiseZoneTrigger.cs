using UnityEngine;

[RequireComponent(typeof(AkAmbient))]
public class WwiseZoneTrigger : MonoBehaviour
{
    public GameObject triggerObject;
    public float stopFadeOutMs = 2000f;

    private AkAmbient _akAmbient;
    private bool isPlaying = false;

    void Awake()
    {
        _akAmbient = GetComponent<AkAmbient>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == triggerObject && !isPlaying)
        {
            _akAmbient.HandleEvent(gameObject);
            isPlaying = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject == triggerObject && isPlaying)
        {
            _akAmbient.data.ExecuteAction(
                gameObject,
                AkActionOnEventType.AkActionOnEventType_Stop,
                (int)stopFadeOutMs,
                AkCurveInterpolation.AkCurveInterpolation_Linear
            );
            isPlaying = false;
        }
    }
}