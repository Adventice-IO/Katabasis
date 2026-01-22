using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

// The contract for any action you want to be undoable
public interface ICommand
{
    void Execute();
    void Undo();
}

// The manager handles the history stacks
public class RuntimeUndoManager : MonoBehaviour
{
    public static RuntimeUndoManager instance;

    private readonly Stack<ICommand> _undoStack = new Stack<ICommand>();
    private readonly Stack<ICommand> _redoStack = new Stack<ICommand>();

    public void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    // Call this to perform a new action
    public void ExecuteCommand(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);

        // Clear redo stack because branching history is complex
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
    }



    //Helpers

    public static void addKnot(Spline spline, int index, BezierKnot knot)
    {
        var command = new AddKnotCommand(spline, knot, index);
        instance.ExecuteCommand(command);
    }

    public static void removeKnot(Spline spline, int index)
    {
        var command = new RemoveKnotCommand(spline, index);
        instance.ExecuteCommand(command);
    }

    public static void changeKnot(Spline spline, int index, BezierKnot newKnot, BezierKnot oldKnot)
    {
        var command = new ChangeKnotCommand(spline, index, newKnot, oldKnot);
        instance.ExecuteCommand(command);
    }

    public static void moveTransform(Transform target, Vector3 newPosition, Quaternion newRotation)
    {
        var command = new TransformCommand(target, newPosition, newRotation);
        instance.ExecuteCommand(command);
    }
}