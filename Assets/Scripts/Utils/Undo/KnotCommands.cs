

using UnityEngine;
using UnityEngine.Splines;

public class AddKnotCommand : ICommand
{
    private readonly SplineContainer _container;
    private readonly BezierKnot _knot;
    private readonly int _index;
    private readonly int _splineIndex; // Usually 0 unless you have multiple splines in one container

    public AddKnotCommand(SplineContainer container, BezierKnot knot, int index, int splineIndex = 0)
    {
        _container = container;
        _knot = knot;
        _index = index;
        _splineIndex = splineIndex;
    }

    public void Execute()
    {
        if (_container.Splines.Count > _splineIndex)
        {
            _container.Splines[_splineIndex].Insert(_index, _knot);
        }
    }

    public void Undo()
    {
        if (_container.Splines.Count > _splineIndex)
        {
            _container.Splines[_splineIndex].RemoveAt(_index);
        }
    }
}

public class RemoveKnotCommand : ICommand
{
    private readonly SplineContainer _container;
    private readonly int _index;
    private readonly int _splineIndex;

    // We must cache the knot data so we can restore it
    private BezierKnot _deletedKnot;

    public RemoveKnotCommand(SplineContainer container, int index, int splineIndex = 0)
    {
        _container = container;
        _index = index;
        _splineIndex = splineIndex;

        // Capture the knot data immediately
        if (_container.Splines.Count > _splineIndex && _index < _container.Splines[_splineIndex].Count)
        {
            _deletedKnot = _container.Splines[_splineIndex][_index];
        }
    }

    public void Execute()
    {
        _container.Splines[_splineIndex].RemoveAt(_index);
    }

    public void Undo()
    {
        // Restore the knot at the exact same index
        _container.Splines[_splineIndex].Insert(_index, _deletedKnot);
    }
}

public class ChangeKnotCommand : ICommand
{ 
    private readonly SplineContainer _container;
    private readonly int _index;
    private readonly BezierKnot _newKnot;
    private readonly int _splineIndex;
    private BezierKnot _oldKnot;
    public ChangeKnotCommand(SplineContainer container, int index, BezierKnot newKnot, int splineIndex = 0)
    {
        _container = container;
        _index = index;
        _newKnot = newKnot;
        _splineIndex = splineIndex;
        // Capture the old knot data immediately
        if (_container.Splines.Count > _splineIndex && _index < _container.Splines[_splineIndex].Count)
        {
            _oldKnot = _container.Splines[_splineIndex][_index];
        }
    }
    public void Execute()
    {
        if (_container.Splines.Count > _splineIndex && _index < _container.Splines[_splineIndex].Count)
        {
            _container.Splines[_splineIndex][_index] = _newKnot;
        }
    }
    public void Undo()
    {
        if (_container.Splines.Count > _splineIndex && _index < _container.Splines[_splineIndex].Count)
        {
            _container.Splines[_splineIndex][_index] = _oldKnot;
        }
    }
}

