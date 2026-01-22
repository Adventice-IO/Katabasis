

using UnityEngine;
using UnityEngine;
using UnityEngine.Splines;

public class AddKnotCommand : ICommand
{
    private readonly Spline spline;
    private readonly BezierKnot _knot;
    private readonly int _index;

    public AddKnotCommand(Spline container, BezierKnot knot, int index, int splineIndex = 0)
    {
        spline = container;
        _knot = knot;
        _index = index;
    }

    public void Execute()
    {
        spline.Insert(_index, _knot);
        spline.SetTangentMode(_index, TangentMode.Continuous);
    }

    public void Undo()
    {
        spline.RemoveAt(_index);
    }
}

public class RemoveKnotCommand : ICommand
{
    private readonly Spline spline;
    private readonly int _index;

    // We must cache the knot data so we can restore it
    private BezierKnot _deletedKnot;

    public RemoveKnotCommand(Spline container, int index, int splineIndex = 0)
    {
        spline = container;
        _index = index;

        // Capture the knot data immediately
        if (spline.Count > 0 && _index < spline.Count)
        {
            _deletedKnot = spline[_index];
        }
    }

    public void Execute()
    {
        spline.RemoveAt(_index);
    }

    public void Undo()
    {
        // Restore the knot at the exact same index
        spline.Insert(_index, _deletedKnot);
        spline.SetTangentMode(_index, TangentMode.Continuous);
    }
}

public class ChangeKnotCommand : ICommand
{
    private readonly Spline spline;
    private readonly int _index;
    private readonly BezierKnot _newKnot;
    private readonly BezierKnot _oldKnot;
    public ChangeKnotCommand(Spline container, int index, BezierKnot newKnot, BezierKnot oldKnot, int splineIndex = 0)
    {
        spline = container;
        _index = index;
        _newKnot = newKnot;
        _oldKnot = oldKnot;
    }
    public void Execute()
    {
        spline.SetKnot(_index, _newKnot);
    }
    public void Undo()
    {
        spline.SetKnot(_index, _oldKnot);
    }
}

