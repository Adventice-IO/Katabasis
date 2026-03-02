

using UnityEngine;
using UnityEngine.Splines;

public class AddCheckpointCommand : ICommand
{
    private readonly Tunnel tunnel;
    private readonly float pos;
    private Tunnel.SpeedCheckpoint checkpoint;

    public AddCheckpointCommand(Tunnel tunnel, float pos)
    {
        this.tunnel = tunnel;
        this.pos = pos;
    }

    public void Execute()
    {
        checkpoint = tunnel.AddSpeedCheckpoint(pos);
    }

    public void Undo()
    {
        tunnel.RemoveSpeedCheckpoint(checkpoint);
    }
}

public class RemoveCheckpointCommand : ICommand
{
    private readonly Tunnel tunnel;
    private readonly Tunnel.SpeedCheckpoint checkpoint;
    public RemoveCheckpointCommand(Tunnel tunnel, Tunnel.SpeedCheckpoint checkpoint)
    {
        this.tunnel = tunnel;
        this.checkpoint = checkpoint;
    }
    public void Execute()
    {
        tunnel.RemoveSpeedCheckpoint(checkpoint);
    }
    public void Undo()
    {
        Tunnel.SpeedCheckpoint cp = tunnel.AddSpeedCheckpoint(checkpoint.pos);
        cp.speed = checkpoint.speed;
    }
}

public class MoveCheckpointCommand : ICommand
{
    private readonly Tunnel tunnel;
    private readonly Tunnel.SpeedCheckpoint checkpoint;
    private readonly float newPos;
    private float oldPos;
    public MoveCheckpointCommand(Tunnel.SpeedCheckpoint checkpoint, float newPos)
    {
        this.checkpoint = checkpoint;
        this.newPos = newPos;
    }
    public void Execute()
    {
        oldPos = checkpoint.pos;
        checkpoint.pos = newPos;
    }
    public void Undo()
    {
        checkpoint.pos = oldPos;
    }
}

public class ChangeCheckpointSpeedCommand : ICommand
{
    private readonly Tunnel.SpeedCheckpoint checkpoint;
    private readonly float newSpeed;
    private float oldSpeed;
    public ChangeCheckpointSpeedCommand(Tunnel.SpeedCheckpoint checkpoint, float oldSpeed, float newSpeed)
    {
        this.checkpoint = checkpoint;
        this.oldSpeed = oldSpeed;
        this.newSpeed = newSpeed;
    }
    public void Execute()
    {
        oldSpeed = checkpoint.speed;
        checkpoint.speed = newSpeed;
    }
    public void Undo()
    {
        oldSpeed = checkpoint.speed;
        checkpoint.speed = oldSpeed;
    }
}

