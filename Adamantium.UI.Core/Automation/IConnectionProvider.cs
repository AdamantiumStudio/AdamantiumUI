using System.Collections.Generic;

namespace Adamantium.UI.Core.Automation;

/// <summary>An end a connection is made to - a socket on a node of a graph - joined to another end and parted from it
/// as a hand pulling a wire would. Automation's own capability: UI Automation has no pattern for it, so a screen reader
/// reads the connections as the element's value.</summary>
public interface IConnectionProvider
{
    /// <summary>True for an end a connection arrives at, false for one it leaves.</summary>
    bool IsInput { get; }

    /// <summary>The ends joined to this one, as they are called: the node, then the socket.</summary>
    IReadOnlyList<string> GetConnections();

    /// <summary>Joins this end to <paramref name="other"/>, by the graph's rules: an output to an input of another node,
    /// of a kind that fits, closing no loop.</summary>
    void Connect(AutomationPeer other);

    /// <summary>Parts this end from <paramref name="other"/>, or from every end it is joined to when that is null.</summary>
    void Disconnect(AutomationPeer other);
}
