using System.Collections.Generic;
using UnityEngine;

public class SpeedFixer : MonoBehaviour
{
    public bool fix;
    bool lastFix;

    void Start()
    {
        
    }

    
    void Update()
    {
        if(fix != lastFix)
        {
            fixSpeeds();
            lastFix = fix;
        }
    }

    void fixSpeeds()
    {
        CheckpointContainer[] speeds = GetComponentsInChildren<CheckpointContainer>();

        foreach(var s in speeds)
        {
            fixSpeedsFor(s);
        }
    }

    void fixSpeedsFor(CheckpointContainer checkpoints)
    {
        List<float> newSpeeds = new List<float>();
        for(var i = 0; i < checkpoints.speedCheckpoints.Count; i++)
        {
            if(i == 0 || i == checkpoints.speedCheckpoints.Count - 1)
            {
                newSpeeds.Add(checkpoints.speedCheckpoints[i].speed);
            }
            else
            {
                float prevSpeed = checkpoints.speedCheckpoints[i - 1].speed;
                float curSpeed = checkpoints.speedCheckpoints[i].speed;
                newSpeeds.Add(prevSpeed + curSpeed);
            }
        }

        for(var i = 0; i < checkpoints.speedCheckpoints.Count; i++)
        {
            checkpoints.speedCheckpoints[i].speed = newSpeeds[i];
        }
    }
}
