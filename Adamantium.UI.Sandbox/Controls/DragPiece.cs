using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Sandbox.Controls;

/// <summary>A clipping-stand piece dragged around its Canvas. Its content is excluded from hit testing so presses reach
/// the piece itself.</summary>
public class DragPiece : ContentControl
{
    private bool _dragging;
    private Vector2 _pressedAt;
    private double _startLeft, _startTop;

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        if (newContent is IUIComponent visual) visual.IsHitTestVisible = false;
    }

    protected override void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _pressedAt = PointerInRoot(e);
        _startLeft = Canvas.GetLeft(this);
        _startTop = Canvas.GetTop(this);
        CaptureMouse();   // the move is routed to the CAPTURED element; without it the piece stops following at its edge
        e.Handled = true;
    }

    // Measured from the PRESS in the ROOT's space, not step by step: the piece moves as it is dragged, so a delta taken
    // against its own position would chase itself.
    protected override void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var delta = PointerInRoot(e) - _pressedAt;
        Canvas.SetLeft(this, _startLeft + delta.X);
        Canvas.SetTop(this, _startTop + delta.Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private Vector2 PointerInRoot(MouseEventArgs e)
    {
        IUIComponent root = this;
        while (root.VisualParent is { } parent) root = parent;
        return root is IInputComponent input ? e.GetPosition(input) : e.GetPosition(this);
    }
}
