using System;
using System.IO;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A named picture the nine-slice stand can be dressed in. A type of its own so the drop-down has something to
/// show and something to hand back - the brush wants an ImageSource, the list wants a name.</summary>
public sealed class NineSliceSkin
{
    public NineSliceSkin(string name, string file)
    {
        Name = name;
        // Rooted against the app's base directory and taken through the cache, the same route the markup form takes:
        // a bare relative path resolves against the WORKING directory and silently fails to decode from an IDE.
        Source = BitmapImageCache.GetOrCreate(Path.Combine(AppContext.BaseDirectory, "Textures", file));
    }

    private NineSliceSkin(string name, ImageSource source)
    {
        Name = name;
        Source = source;
    }

    public string Name { get; }

    /// <summary>ImageSource, not BitmapImage: a nine-slice takes a DRAWING too, and then the picture it cuts is one this
    /// stand asked to be baked rather than one that was decoded.</summary>
    public ImageSource Source { get; }

    /// <summary>A vector skin cut at 0.25, to check that rasterized corners keep their drawn density; circles, since
    /// squares would hide resampling blur.</summary>
    public static NineSliceSkin Vector()
    {
        var edge = new SolidColorBrush(Colors.SteelBlue);
        var centre = new SolidColorBrush(Colors.Black);
        var ornament = new SolidColorBrush(Colors.Orange);

        var group = new DrawingGroup();
        group.Children.Add(Filled(new Rect(0, 0, 64, 64), edge));
        group.Children.Add(Filled(new Rect(16, 16, 32, 32), centre));

        // One in each 16x16 corner cell - the pieces that are drawn at Border and never stretched.
        foreach (var (x, y) in new[] { (8.0, 8.0), (56.0, 8.0), (8.0, 56.0), (56.0, 56.0) })
        {
            group.Children.Add(new GeometryDrawing
            {
                Geometry = new EllipseGeometry { Center = new Vector2(x, y), RadiusX = 6, RadiusY = 6 },
                Brush = ornament
            });
        }

        return new NineSliceSkin("Vector frame (drawing)", new DrawingImage { Drawing = group });
    }

    private static GeometryDrawing Filled(Rect rect, Brush brush) =>
        new() { Geometry = new RectangleGeometry { Rect = rect }, Brush = brush };

    public override string ToString() => Name;
}
