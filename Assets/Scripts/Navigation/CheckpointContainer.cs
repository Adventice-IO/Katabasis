using System.Collections.Generic;
using UnityEngine;

public class CheckpointContainer : MonoBehaviour
{
    public const int CurrentSpeedCheckpointDataVersion = 1;

    [HideInInspector] public int speedCheckpointDataVersion;
    public List<Tunnel.SpeedCheckpoint> speedCheckpoints = new List<Tunnel.SpeedCheckpoint>();
}
