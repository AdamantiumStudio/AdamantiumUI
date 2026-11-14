using System;
using Adamantium.Core;
using Adamantium.MVVM;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>A contour from path data (sub-paths, fill rule, curves and arcs) read through <see cref="SVGParser"/>. Moving and
/// resizing change only its box; the contour is mapped in when drawn.</summary>
public partial class PathItem : AdamantiumViewModel, ICanvasItem, ICanvasTransformed
{
    private string _data;
    private StreamGeometry _geometry;
    private Rect _source;

    public PathItem(string data, Brush fill, Brush stroke, double thickness = 1)
    {
        Data = data;
        Fill = fill;
        Stroke = stroke;
        Thickness = Math.Max(0, thickness);
        World = _source;
    }

    /// <summary>The path data this was made from - SVG's own grammar, kept as it came.</summary>
    public string Data
    {
        get => _data;
        set
        {
            _data = value;
            _geometry = new SVGParser().Parse(_data ?? string.Empty);
            _geometry.RecalculateBounds();
            _source = _geometry.Bounds;
        }
    }

    /// <summary>How a filled contour decides what is inside it - what makes the hole in a ring a hole.</summary>
    public FillRule FillRule
    {
        get => _geometry?.FillRule ?? FillRule.EvenOdd;
        set
        {
            if (_geometry != null) _geometry.FillRule = value;
        }
    }

    public Brush Fill { get; set; }

    public Brush Stroke { get; set; }

    public double Thickness { get; set; }

    /// <summary>Where it stands and how big, in world units. The contour is mapped into this box when it is drawn.
    /// </summary>
    [Bindable, Affects(nameof(X), nameof(Y), nameof(Width), nameof(Height), nameof(Bounds))]
    public partial Rect World { get; set; }

    public CanvasTransform Transform { get; set; } = CanvasTransform.None;

    public int Order { get; set; }

    public string Sort => nameof(PathItem);

    public Rect Bounds => World;

    public Double X
    {
        get => World.X;
        set => World = new Rect(value, World.Y, World.Width, World.Height);
    }

    public Double Y
    {
        get => World.Y;
        set => World = new Rect(World.X, value, World.Width, World.Height);
    }

    public Double Width
    {
        get => World.Width;
        set => World = new Rect(World.X, World.Y, Math.Max(1e-9, value), World.Height);
    }

    public Double Height
    {
        get => World.Height;
        set => World = new Rect(World.X, World.Y, World.Width, Math.Max(1e-9, value));
    }

    public Color? Paint => (Fill as SolidColorBrush)?.Color ?? (Stroke as SolidColorBrush)?.Color;

    public void PaintWith(Color color)
    {
        if (Fill != null) Fill = new SolidColorBrush(color);
        else Stroke = new SolidColorBrush(color);
    }

    public ICanvasItem Copy() =>
        new PathItem(Data, Fill, Stroke, Thickness)
        {
            World = World, Transform = Transform, FillRule = FillRule, Name = Name, Comment = Comment
        };

    /// <summary>The short name a person gave it; empty for none. See <see cref="ICanvasItem.Name"/>.</summary>
    [Bindable, Affects(nameof(Title))]
    public partial string Name { get; set; }

    /// <summary>A longer note about it, shown apart from the name; empty for none.</summary>
    [Bindable]
    public partial string Comment { get; set; }

    /// <summary>Its name, for a list of what is on the plane.</summary>
    public string Title => Name ?? string.Empty;

    public void Move(Vector2<double> worldDelta) =>
        World = new Rect(World.X + worldDelta.X, World.Y + worldDelta.Y, World.Width, World.Height);

    public void Resize(Rect world) =>
        World = new Rect(world.X, world.Y, Math.Max(1e-9, world.Width), Math.Max(1e-9, world.Height));

    /// <summary>Hit when the point is in its box. The contour itself would be the honest answer, and a filled one can
    /// only say so by being tessellated - which is a mesh per question. An imported drawing is picked from a list or
    /// by a band round it far more often than by a press inside one of its holes.</summary>
    public bool HitTest(Vector2<double> world, double tolerance) => World.Inflate(tolerance).Contains(world);

    public Vector2<double> Pivot => new(World.X + World.Width / 2, World.Y + World.Height / 2);

    public void Render(CanvasDrawing drawing)
    {
        if (_geometry == null || World.Width <= 0 || World.Height <= 0) return;

        var sx = _source.Width > 1e-9 ? World.Width / _source.Width : 1;
        var sy = _source.Height > 1e-9 ? World.Height / _source.Height : 1;

        // The pen is stated in the CONTOUR's units, which the fit then scales: divided back out by the fit's mean scale,
        // it is as wide on the plane as the item says where the fit is even, and stretched with the contour where it is
        // not - what a stroke under a scale does in an SVG. Never thinner than a pixel on screen.
        var pen = Stroke != null && Thickness > 0
            ? new Pen(Stroke, Thickness / Math.Sqrt(sx * sy)) { MinimumDeviceThickness = CanvasDrawing.Hairline }
            : null;

        drawing.Session.DrawGeometry(Fill, _geometry, pen, drawing.PartWith(Fit(drawing.Local(World), sx, sy)));
    }

    // The contour states itself in the file's own units; this is what puts it in the box the item stands in.
    private Matrix4x4F Fit(Rect box, double sx, double sy) =>
        Matrix4x4.CreateTranslation<float>((float)-_source.X, (float)-_source.Y, 0)
        * Matrix4x4.CreateScale<float>((float)sx, (float)sy, 1)
        * Matrix4x4.CreateTranslation<float>((float)box.X, (float)box.Y, 0);
}
