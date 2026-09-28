using System;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A wire between two sockets. It adds itself to both on creation and leaves both when cut; its route comes from
/// its nodes, and its ends never change.</summary>
public sealed class CanvasConnection
{
    /// <summary>The socket the wire LEAVES - an output.</summary>
    public ICanvasSocket From { get; }

    /// <summary>The socket it ARRIVES at - an input.</summary>
    public ICanvasSocket To { get; }

    /// <summary>The node it leaves, and the one it arrives at - the socket's own, so a walk never needs a second
    /// account of who holds what.</summary>
    public ICanvasNode FromNode => From?.Node;

    public ICanvasNode ToNode => To?.Node;

    /// <summary>Joins two sockets, and IS the joining: the wire is in both of them by the time this returns.
    /// <para>A socket already holding as many wires as it takes loses its oldest - which is what dropping a new wire on
    /// a taken input means everywhere.</para></summary>
    public CanvasConnection(ICanvasSocket from, ICanvasSocket to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        From = from;
        To = to;

        Seat(from);
        Seat(to);
    }

    /// <summary>Cuts it: gone from both ends, and nothing else holds it.</summary>
    public void Disconnect()
    {
        From?.Connections.Remove(this);
        To?.Connections.Remove(this);
    }

    private void Seat(ICanvasSocket socket)
    {
        // The OLDEST goes, and only as many as have to: a socket that takes three and holds three loses one, not all
        // three, which is the difference between a full socket and a single one.
        while (socket.Capacity > 0 && socket.Connections.Count >= socket.Capacity)
        {
            socket.Connections[0].Disconnect();
        }

        socket.Connections.Add(this);
    }
}
