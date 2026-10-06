using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>Stands for a control on an <see cref="InfiniteCanvas"/> that is off screen, so not on the plane's layer:
/// found by its control's id and name, selected, and brought into view, which puts the control on the layer; from then on
/// its own peer stands for it.</summary>
public class CanvasItemStandInAutomationPeer : AutomationPeer, ISelectionItemProvider, IScrollItemProvider
{
    private readonly InfiniteCanvasAutomationPeer _canvasPeer;
    private readonly InfiniteCanvas _canvas;

    internal CanvasItemStandInAutomationPeer(InfiniteCanvasAutomationPeer canvasPeer, InfiniteCanvas canvas, ElementItem item)
    {
        _canvasPeer = canvasPeer;
        _canvas = canvas;
        Item = item;
    }

    public ElementItem Item { get; }

    public override AutomationControlType ControlType => ElementPeer()?.ControlType ?? AutomationControlType.Custom;

    public override string Name => ElementPeer()?.Name is { Length: > 0 } name ? name : Item.Title;

    public override string AutomationId => ElementPeer()?.AutomationId ?? string.Empty;

    public override string HelpText => string.Empty;

    public override string ClassName => (Item.Painted ?? Item.Element)?.GetType().Name ?? nameof(ElementItem);

    public override Rect BoundingRectangle => Rect.Empty;

    public override bool IsEnabled => _canvasPeer.IsEnabled;

    public override bool IsOffscreen => true;

    public override bool HasKeyboardFocus => false;

    public override bool IsKeyboardFocusable => false;

    public bool IsSelected => _canvas.IsSelected(Item);

    public AutomationPeer SelectionContainer => _canvasPeer;

    public override IReadOnlyList<AutomationPeer> GetChildren() => [];

    public override AutomationPeer GetParent() => _canvasPeer;

    public override void SetFocus()
    {
    }

    public void Select() => _canvas.Select(Item, false);

    /// <summary>Moves the camera so the control is on screen, without zooming - what the canvas's list of everything on
    /// the plane does, short of fitting the view to it.</summary>
    public void ScrollIntoView() => _canvas.BringIntoView(Item.World);

    private AutomationPeer ElementPeer() => ((Item.Painted ?? Item.Element) as UIComponent)?.GetAutomationPeer();
}
