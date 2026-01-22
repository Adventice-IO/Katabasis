using System.Collections.Generic;
using UnityEngine;

// The contract for any action you want to be undoable
public interface ICommand
{
    void Execute();
    void Undo();
}

// The manager handles the history stacks
public class RuntimeUndoManager : MonoBehaviour
{
    private readonly Stack<ICommand> _undoStack = new Stack<ICommand>();
    private readonly Stack<ICommand> _redoStack = new Stack<ICommand>();

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
}