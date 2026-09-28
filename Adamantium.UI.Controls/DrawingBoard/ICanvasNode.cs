using System.ComponentModel;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A graph node as the application holds it; the sort is data (<see cref="Kind"/>). The canvas makes a
/// <see cref="CanvasNode"/> for each and keeps the collection in step both ways.</summary>
public interface ICanvasNode : ICanvasPlaced
{
    /// <summary>What sort of node this is in the application's words ("Multiply"): a string, so the canvas can make nodes and
    /// a kind change keeps the object.</summary>
    string Kind { get; set; }

    /// <summary>What the strip says. Written as well as read: renaming a node in an inspector belongs to the node.
    /// </summary>
    string Title { get; set; }

    /// <summary>The node's strip color, part of the document; a color rather than a shareable brush. Null wears the theme's.</summary>
    Color? Accent { get; set; }

    /// <summary>Folded down to its title strip.</summary>
    bool IsCollapsed { get; set; }

    /// <summary>The sockets down each side. Observable because an inspector adds and drops them one at a time, and a
    /// wire has to hear about the socket it is sitting on going away.</summary>
    TrackingCollection<ICanvasSocket> Inputs { get; }

    TrackingCollection<ICanvasSocket> Outputs { get; }

    /// <summary>A NEW socket for this node, named but not yet placed - what the inspector's plus asks for.
    /// <para>Asked of the NODE because a socket is the application's object exactly as a node is: the canvas holds no
    /// socket type of its own to fall back on. NULL means "sockets are not added by hand here", and then the plus is
    /// simply not offered - which is a real answer for a node whose sockets follow from what it does.</para></summary>
    ICanvasSocket NewSocket(string name) => null;

    /// <summary>WHAT THIS NODE IS - the part that differs between kinds, and the only part a change of kind replaces.
    /// It shapes the sockets when it is installed and stands as the node's content, drawn by the template chosen for
    /// its type. The canvas never looks inside it.</summary>
    ICanvasNodeSpecialization Specialization { get; set; }
}
