using System.ComponentModel;
using Adamantium.Core.Collections;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One socket of a node as the application holds it; its wires live here, so the graph walks both ways. Its color
/// comes from its kind.</summary>
public interface ICanvasSocket : INotifyPropertyChanged
{
    /// <summary>What it is called. A wire is written down as the two names it joins, so this is also its address.
    /// </summary>
    string Name { get; set; }

    /// <summary>What flows through it - "Color", "Number". Two sockets join when their kinds agree; one that says
    /// nothing takes anything. Whether they agree is the application's rule, not the engine's.</summary>
    string Kind { get; set; }

    /// <summary>The node this belongs to, stamped when it joins one. Without it a walk up the graph stops at the far
    /// socket with no way of asking whose it is.</summary>
    ICanvasNode Node { get; set; }

    /// <summary>How many wires may sit here, 0 for unlimited; a wire dropped on a full socket displaces the oldest.</summary>
    int Capacity { get; set; }

    /// <summary>The wires sitting on it. A LIST and not a set: in a socket that takes many, the order of the wires is
    /// part of the answer.</summary>
    TrackingCollection<CanvasConnection> Connections { get; }
}
