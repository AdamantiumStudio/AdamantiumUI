using System;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Decorators;

/// <summary>
/// Shows a part of its child along one axis - <see cref="Progress"/> of its full size - and takes only that much room, so
/// whatever lies past it moves as the part grows: what an <see cref="Expander"/> unfolds its content with. The child keeps
/// its full size - or the room it is given, when that is more - and stays against the side it opens from
/// (<see cref="Direction"/>); the rest is clipped. At 0 the child is not measured.
/// </summary>
public class RevealDecorator : Decorator
{
    public static readonly AdamantiumProperty ProgressProperty = AdamantiumProperty.Register(nameof(Progress),
        typeof(double), typeof(RevealDecorator), new PropertyMetadata(1.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty DirectionProperty = AdamantiumProperty.Register(nameof(Direction),
        typeof(ExpandDirection), typeof(RevealDecorator),
        new PropertyMetadata(ExpandDirection.Down, PropertyMetadataOptions.AffectsMeasure));

    static RevealDecorator()
    {
        ClipToBoundsProperty.OverrideMetadata(typeof(RevealDecorator), new PropertyMetadata(true));
    }

    /// <summary>How much of the child shows, from 0 - none, taking no room - to 1, all of it.</summary>
    public double Progress
    {
        get => GetValue<double>(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Which way the child opens: down and up reveal its height, right and left its width; it stays against the
    /// side it opens from.</summary>
    public ExpandDirection Direction
    {
        get => GetValue<ExpandDirection>(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    private bool OpensVertically => Direction is ExpandDirection.Down or ExpandDirection.Up;

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var progress = Progress;
        if (progress <= 0)
        {
            return default;
        }

        var full = base.MeasureOverride(availableSize);
        if (progress >= 1)
        {
            return full;
        }

        return OpensVertically
            ? new Size<double>(full.Width, full.Height * progress)
            : new Size<double>(full.Width * progress, full.Height);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var progress = Progress;
        if (progress >= 1 || Child is not { } child)
        {
            return base.ArrangeOverride(finalSize);
        }

        if (progress <= 0)
        {
            return default;
        }

        var vertical = OpensVertically;
        var full = child.DesiredSize.Inflate(Padding);
        var given = vertical ? finalSize.Height : finalSize.Width;
        var whole = Math.Max(vertical ? full.Height : full.Width, given);
        var shown = Math.Min(given, whole * progress);
        var offset = Direction is ExpandDirection.Up or ExpandDirection.Left ? shown - whole : 0;
        var slot = vertical
            ? new Rect(0, offset, finalSize.Width, whole)
            : new Rect(offset, 0, whole, finalSize.Height);
        child.Arrange(slot.Deflate(Padding));
        return vertical ? new Size<double>(finalSize.Width, shown) : new Size<double>(shown, finalSize.Height);
    }
}
