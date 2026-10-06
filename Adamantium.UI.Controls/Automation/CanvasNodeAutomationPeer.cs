using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CanvasNode"/>: an item of the canvas's graph, called by its title, selected as a
/// click selects it, moved and resized on the plane as one undoable step each.</summary>
public class CanvasNodeAutomationPeer : ContentControlAutomationPeer, ITransformProvider, ISelectionItemProvider
{
    public CanvasNodeAutomationPeer(CanvasNode owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.DataItem;

    public bool CanMove => Placed().Item != null;

    public bool CanResize => Placed().Item != null;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    public void Move(double x, double y)
    {
        var bounds = BoundingRectangle;
        Edit("Move", (item, worldPerPixel) =>
            item.Move(new Vector2((x - bounds.X) * worldPerPixel, (y - bounds.Y) * worldPerPixel)));
    }

    public void Resize(double width, double height) =>
        Edit("Resize", (item, worldPerPixel) =>
            item.Resize(new Rect(item.World.X, item.World.Y, width * worldPerPixel, height * worldPerPixel)));

    public void Zoom(double percent) => throw new InvalidOperationException("A node is not zoomed; its canvas is.");

    public bool IsSelected => Placed() is ({ } canvas, { } item) && canvas.IsSelected(item);

    public AutomationPeer SelectionContainer => Placed().Canvas?.GetAutomationPeer();

    public void Select()
    {
        var (canvas, item) = Placed();
        if (item == null)
        {
            throw new InvalidOperationException("The node is not on a canvas.");
        }

        canvas.Select(item, false);
    }

    protected override string NameCore() => ((CanvasNode)Owner).Title as string ?? TextOf(Owner);

    /// <summary>The canvas the node stands on and the item that places it there; nulls for a node off any canvas.</summary>
    internal (InfiniteCanvas Canvas, ElementItem Item) Placed()
    {
        IUIComponent child = Owner;
        for (var parent = child.VisualParent; parent != null; child = parent, parent = parent.VisualParent)
        {
            if (parent is CanvasElementLayer { Owner: { } canvas } layer)
            {
                return (canvas, layer.ItemOf(child));
            }
        }

        return (null, null);
    }

    private void Edit(string reason, Action<ElementItem, double> change)
    {
        var (canvas, item) = Placed();
        if (item == null)
        {
            throw new InvalidOperationException("The node is not on a canvas.");
        }

        var bounds = BoundingRectangle;
        canvas.BeginEdit(reason);
        change(item, item.World.Width / bounds.Width);
        canvas.Scene?.Touch();
        canvas.EndEdit();
        canvas.Repaint();
    }
}
