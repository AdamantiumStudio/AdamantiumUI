using System.Collections.Generic;

namespace Adamantium.UI.Controls.DrawingBoard;

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
    }

    public string Reason { get; }

    /// <summary>Whether any of the objects actually took a different value. A line re-typed with the same number in it
    /// must not become a step that undoes to what it already was.</summary>
    public bool IsSomething
    {
        get
        {
            foreach (var (_, was, now) in _values)
            {
                if (!Equals(was, now)) return true;
            }

            return false;
        }
    }

    public void Apply(ICanvasScene scene, bool forward)
    {
        foreach (var (target, was, now) in _values) _grid.WriteTo(target, _definition, forward ? now : was);

        // Plane objects raise no change events, so tell both the drawing and the panel; the rows are rebuilt, since
        // re-reading returns their stale values.
        scene?.Touch();
        _grid.Rebuild();
    }
}
