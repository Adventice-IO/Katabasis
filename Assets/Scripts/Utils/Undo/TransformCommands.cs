
using UnityEngine;

public class TransformCommand : ICommand
{
    private readonly Transform _target;
    private readonly Vector3 _newPosition;
    private readonly Quaternion _newRotation;

    // State before the action
    private Vector3 _prevPosition;
    private Quaternion _prevRotation;

    public TransformCommand(Transform target, Vector3 newPos, Quaternion newRot)
    {
        _target = target;
        _newPosition = newPos;
        _newRotation = newRot;

        // Capture current state immediately upon creation
        _prevPosition = target.position;
        _prevRotation = target.rotation;
    }

    public void Execute()
    {
        // Update previous state in case this is a redo
        _target.position = _newPosition;
        _target.rotation = _newRotation;
    }

    public void Undo()
    {
        _target.position = _prevPosition;
        _target.rotation = _prevRotation;
    }
}
