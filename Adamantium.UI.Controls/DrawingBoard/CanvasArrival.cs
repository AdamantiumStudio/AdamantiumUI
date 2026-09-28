using System.Collections.Generic;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What came in on one socket: its kind and the values its wires brought (none, one or many), so a node reads
/// arrivals by kind.</summary>
public readonly struct CanvasArrival
{
    public CanvasArrival(string kind, IReadOnlyList<object> values)
    {
        Kind = kind;
        Values = values;
    }

    /// <summary>What flows through the socket, in the application's own words.</summary>
    public string Kind { get; }

    /// <summary>What its wires brought, in the order the wires sit on it - empty for a socket nothing is joined to.
    /// </summary>
    public IReadOnlyList<object> Values { get; }
}
