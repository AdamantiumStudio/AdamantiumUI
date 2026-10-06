using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="InfiniteCanvas"/>: a pane zoomed about the middle of what it shows, whose
/// selection is the controls on it that are selected.</summary>
public class InfiniteCanvasAutomationPeer : PaneAutomationPeer, ITransformProvider, ISelectionProvider
{
    private readonly InfiniteCanvas _canvas;

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
            .Select(item => (item.Painted as UIComponent)?.GetAutomationPeer())
            .Where(peer => peer != null)
    ];
}
