using System;
using Adamantium.Core;
using Adamantium.MVVM;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The box round everything selected, as one object: what an inspector shows for several items at once. The
/// canvas updates it once per edit however many items moved, and writing a number moves or scales the whole selection
/// as the frame's grips do - one step of undo.</summary>
public sealed class CanvasSelectionFrame : AdamantiumViewModel
{
    private readonly InfiniteCanvas _canvas;
    private Rect _box;

    internal CanvasSelectionFrame(InfiniteCanvas canvas)
    {
        _canvas = canvas;
    }

    public Double X
    {
        get => _box.X;
        set => _canvas.MoveSelectionTo(value, null);
    }

    public Double Y
    {
        get => _box.Y;
        set => _canvas.MoveSelectionTo(null, value);
    }

    public Double Width
    {
        get => _box.Width;
        set => _canvas.ResizeSelectionTo(value, null);
    }

    public Double Height
    {
        get => _box.Height;
        set => _canvas.ResizeSelectionTo(null, value);
    }

    public Rect Bounds => _box;

    internal void Follow(Rect box)
    {
        var was = _box;
        if (was.Equals(box))
        {
            return;
        }

        _box = box;

        if (was.X != box.X)
        {
            RaisePropertyChanged(nameof(X));
        }

        if (was.Y != box.Y)
        {
            RaisePropertyChanged(nameof(Y));
        }

        if (was.Width != box.Width)
        {
            RaisePropertyChanged(nameof(Width));
        }

        if (was.Height != box.Height)
        {
            RaisePropertyChanged(nameof(Height));
        }

        RaisePropertyChanged(nameof(Bounds));
    }
}
