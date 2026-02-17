
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

    public TransformCommand(Transform target, Vector3 oldPos, Quaternion oldRot, Vector3 newPos, Quaternion newRot)
    {
        _target = target;
        _newPosition = newPos;
        _newRotation = newRot;

        // Capture current state immediately upon creation
        _prevPosition = oldPos;
        _prevRotation = oldRot;
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

public class ScaleCommand : ICommand
{
    private readonly Transform _target;
    private readonly Vector3 _newScale;
    // State before the action
    private Vector3 _prevScale;
    public ScaleCommand(Transform target, Vector3 prevScale, Vector3 newScale)
    {
        _target = target;
        _newScale = newScale;
        // Capture current state immediately upon creation
        _prevScale = prevScale;
    }
    public void Execute()
    {
        _target.localScale = _newScale;
    }
    public void Undo()
    {
        _target.localScale = _prevScale;
    }
}

public class ResizeColliderCommand : ICommand
{
    private readonly SphereCollider _target;
    private readonly float _newRadius;
    // State before the action
    private float _prevRadius;
    public ResizeColliderCommand(SphereCollider target, float prevRadius,  float newRadius)
    {
        _target = target;
        _newRadius = newRadius;
        // Capture current state immediately upon creation
        _prevRadius = prevRadius;
    }
    public void Execute()
    {
        _target.radius = _newRadius;
    }
    public void Undo()
    {
        _target.radius = _prevRadius;
    }
}
