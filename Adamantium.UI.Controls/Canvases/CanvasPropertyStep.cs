using System.Collections.Generic;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>An undo step for one inspector line: old and new values per object, restored through the line's binding. A
/// property change moves nothing, so the drawing comparison cannot see it.</summary>
public sealed class CanvasPropertyStep : ICanvasStep
{
    private readonly PropertyGrid _grid;
    private readonly PropertyDefinition _definition;
    private readonly List<(object Target, object Was, object Is)> _values;

    public CanvasPropertyStep(PropertyGrid grid, PropertyDefinition definition,
        List<(object Target, object Was, object Is)> values)
    {
        _grid = grid;
        _definition = definition;
        _values = values;
        Reason = definition?.Header as string ?? "Property";
        var targets = new List<object>(values?.Count ?? 0);
        foreach (var value in values ?? [])
        {
            targets.Add(value.Target);
        }

        Targets = targets;
    }

    public string Reason { get; }

    /// <summary>The objects the line was written to - what an undo or a redo of this step changes.</summary>
    public IReadOnlyList<object> Targets { get; }

    /// <summary>Whether any of the objects actually took a different value. A line re-typed with the same number in it
    /// must not become a step that undoes to what it already was.</summary>
    public bool IsSomething
    {
        get
        {
            foreach (var (_, was, now) in _values)
            {
                if (_definition == null ? !Equals(was, now) : !_definition.SameValue(was, now))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Apply(ICanvasScene scene, bool forward)
    {
        foreach (var (target, was, now) in _values)
        {
            _grid.WriteTo(target, _definition, _definition?.Remembered(forward ? now : was));
        }

        // Plane objects say only where they are and how big, so tell both the drawing and the panel; the rows are
        // rebuilt, since re-reading returns their stale values.
        scene?.Touch();
        _grid.Rebuild();
    }

    /// <summary>The same values written to the items that now stand for the ones it was written to; a value that is a
    /// place on the plane is kept as it was.</summary>
    public ICanvasStep Remapped(ICanvasRemap remap)
    {
        var values = new List<(object Target, object Was, object Is)>(_values.Count);
        foreach (var (target, was, now) in _values)
        {
            if (target is not ICanvasItem item)
            {
                values.Add((target, was, now));
                continue;
            }

            if (remap.ItemFor(item) is not { } carried)
            {
                return null;
            }

            values.Add((carried, was, now));
        }

        return new CanvasPropertyStep(_grid, _definition, values);
    }
}
