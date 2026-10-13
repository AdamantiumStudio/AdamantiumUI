using System;

namespace Adamantium.UI.Core.Media.Drawings;

internal sealed class BrushChangeRelay
{
    private readonly Brush _brush;
    private readonly WeakReference<Drawing> _drawing;

    public BrushChangeRelay(Brush brush, Drawing drawing)
    {
        _brush = brush;
        _drawing = new WeakReference<Drawing>(drawing);
        brush.Changed += OnChanged;
    }

    public void Release() => _brush.Changed -= OnChanged;

    private void OnChanged(object sender, EventArgs e)
    {
        if (_drawing.TryGetTarget(out var drawing))
        {
            drawing.BrushChanged();
        }
        else
        {
            Release();
        }
    }
}
