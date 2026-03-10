using UnityEngine;

public class GameOutro : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    bool isActive = false;
    float timeOnActiveChange = 0f;

    float autoTime = 2f;

    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {

        if (isActive)
        {
            float timeSinceActiveChange = Time.time - timeOnActiveChange;
            if (timeSinceActiveChange > autoTime)
            {
                MainController.instance.gameState = MainController.GameState.End;
            }
        }
    }

    public void setActive(bool active)
    {
        isActive = active;
        timeOnActiveChange = Time.time;
    }
}
