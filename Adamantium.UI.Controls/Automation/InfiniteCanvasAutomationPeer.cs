using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="InfiniteCanvas"/>: a pane zoomed about the middle of what it shows, whose
/// selection is the controls on it that are selected. A control of the canvas's mode that is off screen is a child too,
/// by a stand-in.</summary>
public class InfiniteCanvasAutomationPeer : PaneAutomationPeer, ITransformProvider, ISelectionProvider
{
    private readonly InfiniteCanvas _canvas;
    private readonly Dictionary<ElementItem, CanvasItemStandInAutomationPeer> _standIns = new();

    public InfiniteCanvasAutomationPeer(InfiniteCanvas owner) : base(owner)
    {
        _canvas = owner;
    }

    public bool CanMove => false;

    public bool CanResize => false;

    public bool CanZoom => true;

    public double ZoomLevel => _canvas.Scale * 100;

    public double ZoomMinimum => _canvas.MinScale * 100;

    public double ZoomMaximum => _canvas.MaxScale * 100;

    public bool CanSelectMultiple => true;

    public bool IsSelectionRequired => false;

    public void Move(double x, double y) => throw new InvalidOperationException("A canvas is not moved; its nodes are.");

    public void Resize(double width, double height) => throw new InvalidOperationException("A canvas is not resized.");

    public void Zoom(double percent)
    {
        var room = _canvas.UsableBounds;
        _canvas.SetScaleAt(new Vector2(room.X + room.Width / 2, room.Y + room.Height / 2), percent / 100);
    }

    /// <summary>The selected controls; ink and shapes, which are not elements, have no peer to stand in it.</summary>
    public IReadOnlyList<AutomationPeer> GetSelection() =>
    [
        .. _canvas.Selection.OfType<ElementItem>()
            .Select(PeerOf)
            .Where(peer => peer != null)
    ];

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>(base.ChildrenCore());
        var shown = children.ToHashSet();
        var offScreen = _canvas.ItemsHere().OfType<ElementItem>()
            .Where(item => (item.Painted as UIComponent)?.FindAutomationPeer() is not { } peer || !shown.Contains(peer))
            .ToHashSet();
        foreach (var gone in _standIns.Keys.Where(item => !offScreen.Contains(item)).ToList())
        {
            _standIns.Remove(gone);
        }

        children.AddRange(offScreen.Select(StandInFor));
        return children;
    }

    private AutomationPeer PeerOf(ElementItem item) =>
        _standIns.TryGetValue(item, out var standIn) ? standIn : (item.Painted as UIComponent)?.GetAutomationPeer();

    private CanvasItemStandInAutomationPeer StandInFor(ElementItem item)
    {
        if (!_standIns.TryGetValue(item, out var standIn))
        {
            standIn = new CanvasItemStandInAutomationPeer(this, _canvas, item);
            _standIns[item] = standIn;
        }

        return standIn;
    }
}
