using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>A field's parts in a row. Too narrow for them all, it slides them along so the part being typed stays in
/// view, and cuts them at its edges; with no part being typed it shows them from the start.</summary>
public class FieldSegmentPanel : Panel
{
    private double _offset;

    public FieldSegmentPanel()
    {
        ClipToBounds = true;
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size<double>(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size<double>(width, height);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        double total = 0, start = 0, end = 0;
        var typing = false;
        foreach (var child in Children)
        {
            if (child is FieldSegment { IsActive: true })
            {
                typing = true;
                start = total;
                end = total + child.DesiredSize.Width;
            }

            total += child.DesiredSize.Width;
        }

        if (!typing)
        {
            _offset = 0;
        }
        else
        {
            if (end - _offset > finalSize.Width) _offset = end - finalSize.Width;
            if (start < _offset) _offset = start;
        }

        _offset = Math.Clamp(_offset, 0, Math.Max(0, total - finalSize.Width));
        var x = -_offset;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }

        return finalSize;
    }
}
