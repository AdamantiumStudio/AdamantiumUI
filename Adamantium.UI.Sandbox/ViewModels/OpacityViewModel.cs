using System;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Opacity tab: checks every drawing family fades equally, with one swatch strip in a faded container beside an
/// unfaded copy; a family that fades twice or not at all stands out.</summary>
[ViewModel]
public partial class OpacityViewModel : TabPageViewModel
{
    public OpacityViewModel() : base("Opacity") { }

    /// <summary>Fades the CONTAINER - an ancestor's opacity, which is the case that travels through the slot.</summary>
    [Bindable] private double _containerOpacity = 0.5;

    /// <summary>Fades one swatch inside that container ON TOP of the container's own fade. The product is what should
    /// reach the screen; a family that reads the slot AND keeps the chain in its colour shows here as too dark.</summary>
    [Bindable] private double _elementOpacity = 1;

    /// <summary>The MESH family's swatch - tessellated triangles rather than a shader-side shape, which is a different
    /// path to the same question. Sized to the swatch so the piece and its bounds agree.</summary>
    public PointsCollection Star { get; } = MakeStar(84, 56);

    // Ten points on a circle, alternating the full radius and the 0.382 that reads as a star, then fitted into the box.
    private static PointsCollection MakeStar(double width, double height)
    {
        const double innerRatio = 0.382;
        var unit = new Vector2[10];
        var min = new Vector2(double.MaxValue, double.MaxValue);
        var max = new Vector2(double.MinValue, double.MinValue);
        for (var i = 0; i < unit.Length; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? 1.0 : innerRatio;
            unit[i] = new Vector2(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
            min = Vector2.Min(min, unit[i]);
            max = Vector2.Max(max, unit[i]);
        }

        var scale = Math.Min(width / (max.X - min.X), height / (max.Y - min.Y));
        var points = new Vector2[unit.Length];
        for (var i = 0; i < unit.Length; i++)
        {
            points[i] = new Vector2((unit[i].X - min.X) * scale, (unit[i].Y - min.Y) * scale);
        }
        return new PointsCollection(points);
    }
}
