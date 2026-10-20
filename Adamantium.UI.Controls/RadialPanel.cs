using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

internal enum RadialAnchor
{
    Center,
    Hand,
    Disc,
}

internal sealed class RadialPanel : Panel
{
    private readonly Dictionary<IUIComponent, Place> _places = new();

    public void Add(MeasurableUIComponent child, double degrees, double radius, RadialAnchor anchor = RadialAnchor.Center)
    {
        _places[child] = new Place(degrees, radius, anchor);
        Children.Add(child);
    }

    public void Clear()
    {
        _places.Clear();
        Children.Clear();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        var side = Math.Min(availableSize.Width, availableSize.Height);
        return double.IsInfinity(side) ? Size.Zero : new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var center = new Vector2(finalSize.Width / 2, finalSize.Height / 2);
        var outer = Math.Min(finalSize.Width, finalSize.Height) / 2;
        foreach (var child in Children)
        {
            if (!_places.TryGetValue(child, out var place))
            {
                child.Arrange(new Rect(finalSize));
                continue;
            }

            var size = child.DesiredSize;
            var length = place.Radius * outer;
            if (place.Anchor == RadialAnchor.Hand)
            {
                child.Arrange(new Rect(center.X - size.Width / 2, center.Y - length, size.Width, length));
                continue;
            }

            if (place.Anchor == RadialAnchor.Disc)
            {
                child.Arrange(new Rect(center.X - length, center.Y - length, 2 * length, 2 * length));
                continue;
            }

            var radians = place.Degrees * Math.PI / 180;
            var x = center.X + length * Math.Sin(radians);
            var y = center.Y - length * Math.Cos(radians);
            child.Arrange(new Rect(x - size.Width / 2, y - size.Height / 2, size.Width, size.Height));
        }

        return finalSize;
    }

    private readonly record struct Place(double Degrees, double Radius, RadialAnchor Anchor);
}
