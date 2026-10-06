using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CanvasNodeSocket"/>: a socket of a node, called by its pin's name, joined to and
/// parted from another socket as a wire pulled by hand would be - each as one step of undo. Its value says what it is
/// joined to.</summary>
public class CanvasSocketAutomationPeer : UIComponentAutomationPeer, IConnectionProvider, IValueProvider
{
    public CanvasSocketAutomationPeer(CanvasNodeSocket owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Thumb;

    public bool IsInput => Pin?.IsInput == true;

    public string Value => string.Join(", ", GetConnections());

    public bool IsReadOnly => true;

    public void SetValue(string value) => throw new InvalidOperationException("A socket is joined with Connect, not written.");

    public IReadOnlyList<string> GetConnections()
    {
        var (canvas, _, pin) = End();
        if (canvas == null)
        {
            return [];
        }

        return
        [
            .. Wires(canvas, pin).Select(wire => ReferenceEquals(wire.FromPin, pin)
                ? NameOf(wire.ToItem, wire.ToPin)
                : NameOf(wire.FromItem, wire.FromPin))
        ];
    }

    public void Connect(AutomationPeer other)
    {
        var (canvas, item, pin) = End();
        var (_, otherItem, otherPin) = (other as CanvasSocketAutomationPeer)?.End() ?? default;
        if (canvas == null || otherPin == null)
        {
            throw new InvalidOperationException("Both ends must be sockets of nodes on a canvas.");
        }

        canvas.BeginEdit("Connect");
        var wire = canvas.Join(item, pin, otherItem, otherPin);
        canvas.EndEdit();
        if (wire == null)
        {
            throw new InvalidOperationException(
                $"'{NameOf(item, pin)}' cannot be joined to '{NameOf(otherItem, otherPin)}': an output joins an input of " +
                "another node, of a kind that fits, closing no loop.");
        }

        canvas.Repaint();
    }

    public void Disconnect(AutomationPeer other)
    {
        var (canvas, _, pin) = End();
        if (canvas == null)
        {
            throw new InvalidOperationException("The socket is not on a canvas.");
        }

        var otherPin = (other as CanvasSocketAutomationPeer)?.Pin;
        var parted = Wires(canvas, pin)
            .Where(wire => otherPin == null || ReferenceEquals(wire.FromPin, otherPin) || ReferenceEquals(wire.ToPin, otherPin))
            .ToList();
        canvas.BeginEdit("Disconnect");
        foreach (var wire in parted)
        {
            ConnectionItem.Cut(canvas.Scene, wire);
        }

        canvas.EndEdit();
        canvas.Repaint();
    }

    protected override string NameCore() => Pin?.Name;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private CanvasNodePin Pin => ((CanvasNodeSocket)Owner).Pin;

    /// <summary>The canvas, the node's item and the pin this socket stands for; nulls off a canvas.</summary>
    internal (InfiniteCanvas Canvas, ElementItem Item, CanvasNodePin Pin) End()
    {
        for (var node = Owner.VisualParent; node != null; node = node.VisualParent)
        {
            if (node is CanvasNode canvasNode && canvasNode.GetAutomationPeer() is CanvasNodeAutomationPeer nodePeer)
            {
                var (canvas, item) = nodePeer.Placed();
                return item == null || Pin == null ? default : (canvas, item, Pin);
            }
        }

        return default;
    }

    private static IEnumerable<ConnectionItem> Wires(InfiniteCanvas canvas, CanvasNodePin pin) =>
        canvas.ItemsHere().OfType<ConnectionItem>()
            .Where(wire => ReferenceEquals(wire.FromPin, pin) || ReferenceEquals(wire.ToPin, pin));

    private static string NameOf(ElementItem item, CanvasNodePin pin)
    {
        var node = ((item.Painted ?? item.Element) as UIComponent)?.GetAutomationPeer()?.Name;
        return $"{(string.IsNullOrEmpty(node) ? item.Title : node)}: {pin.Name}";
    }
}
