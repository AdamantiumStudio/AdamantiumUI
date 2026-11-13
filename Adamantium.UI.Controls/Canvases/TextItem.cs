using System;
using Adamantium.Core;
using Adamantium.MVVM;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Words on the plane: a piece of text at a place in the world, in a size measured in WORLD units - so it is
/// part of the drawing and grows with the zoom, the way a label written on paper does.
/// <para>Which is why it is shaped at the size it is DRAWN at rather than once: a glyph laid out for one size and then
/// scaled is a blurred glyph, and the whole reason this engine keeps text as glyphs is not to do that.</para></summary>
public partial class TextItem : AdamantiumViewModel, ICanvasItem, ICanvasTransformed
{
    // Two layouts: the drawing keeps a reference to its layout, so measuring at world size must not reshape it.
    private TextLayout _draw;
    private TextLayout _measure;
    private FontFamily _layoutFont;
    private string _shapedText;
    private double _shapedSize = double.NaN;
    private Size<double> _measured;
    private string _drawnText;
    private double _drawnSize = double.NaN;
    private Size<double> _drawn;

    public TextItem(Vector2<double> origin, string text, Brush brush, double fontSize, FontFamily font = null)
    {
        Origin = origin;
        Text = text ?? string.Empty;
        Brush = brush;
        FontSize = fontSize;
        FontFamily = font;
    }

    /// <summary>A second piece of text just like this one.</summary>
    public ICanvasItem Copy() => new TextItem(Origin, Text, Brush?.Copy(), FontSize, FontFamily) { Transform = Transform };

    /// <summary>How it is turned and leaned, about the middle of its own box. <see cref="Bounds"/> stays the box of the
    /// text unturned, as a shape's does.</summary>
    public CanvasTransform Transform { get; set; } = CanvasTransform.None;

    /// <summary>The middle of its box, which it is turned about.</summary>
    public Vector2<double> Pivot
    {
        get
        {
            var box = Bounds;
            return new Vector2<double>(box.X + box.Width / 2, box.Y + box.Height / 2);
        }
    }

    /// <summary>Where the text starts, in the world - its top-left corner.</summary>
    [Bindable, Affects(nameof(X), nameof(Y), nameof(Bounds))]
    public partial Vector2<double> Origin { get; set; }

    public string Text { get; set; }

    public Brush Brush { get; set; }

    /// <summary>The color the letters are drawn in.</summary>
    public Color? Paint => (Brush as SolidColorBrush)?.Color;

    public void PaintWith(Color color) => Brush = new SolidColorBrush(color);

    /// <summary>The height of the letters in WORLD units.</summary>
    [Bindable, Affects(nameof(Width), nameof(Height), nameof(Bounds))]
    public partial double FontSize { get; set; }

    /// <summary>The face, or nothing for the application's own.</summary>
    public FontFamily FontFamily { get; set; }

    /// <summary>How tall ONE line is, in world units - what a caret is drawn to. The box a piece of text covers is not
    /// that: it is as tall as however many lines there are, and for a caret the line is the answer.</summary>
    public double LineHeight
    {
        get
        {
            var lines = 1;
            foreach (var character in Text ?? string.Empty)
            {
                if (character == '\n') lines++;
            }

            var size = MeasureAt(FontSize);
            return size.Height > 0 ? size.Height / lines : FontSize;
        }
    }

    /// <summary>Where it is and how big, one number at a time - see <see cref="StrokeItem.X"/>. Setting a size here
    /// sets the FONT size, because that is what resizing a piece of text means.</summary>
    public double X
    {
        get => Bounds.X;
        set => Move(new Vector2<double>(value - Bounds.X, 0));
    }

    public double Y
    {
        get => Bounds.Y;
        set => Move(new Vector2<double>(0, value - Bounds.Y));
    }

    public double Width
    {
        get => Bounds.Width;
        set => Resize(new Rect(Bounds.X, Bounds.Y, Math.Max(1e-9, value), Bounds.Height));
    }

    public double Height
    {
        get => Bounds.Height;
        set => Resize(new Rect(Bounds.X, Bounds.Y, Bounds.Width, Math.Max(1e-9, value)));
    }

    /// <summary>What it says, cut short. A list of pieces of text where every row reads "Text" is a list of nothing.
    /// </summary>
    public string Title => string.IsNullOrWhiteSpace(Text)
        ? string.Empty
        : Text.Length <= 24 ? Text : Text[..24] + "...";


    /// <summary>Where it stands in paint order - stamped by the scene. See ICanvasItem.Order.</summary>
    public int Order { get; set; }

    public Rect Bounds
    {
        get
        {
            // Measured at ONE to one, so a box does not depend on where the camera is - what it covers on the plane is a
            // fact about the drawing. What is drawn is shaped at the camera's size, which is a different question.
            var size = MeasureAt(FontSize);
            return new Rect(Origin.X, Origin.Y, size.Width, size.Height);
        }
    }

    public void Move(Vector2<double> worldDelta) => Origin += worldDelta;

    /// <summary>Resizing text sets its SIZE, not a scale on top of it: a box twice as tall is text twice as big, which is
    /// the same thing a designer means by dragging the corner of a label.</summary>
    public void Resize(Rect world)
    {
        if (world.Height <= 0) return;

        var was = MeasureAt(FontSize);
        if (was.Height <= 0) return;

        // Only away from zero: a floor in world units is a floor on the ZOOM - typed at 1000x the text is a hundredth
        // of a unit tall, and 0.01 stopped it a third of the way down.
        FontSize = Math.Max(1e-6, FontSize * world.Height / was.Height);
        Origin = new Vector2<double>(world.X, world.Y);
    }

    public bool HitTest(Vector2<double> world, double tolerance)
    {
        if (Transform.IsSomething) world = Transform.Undo(world, Pivot);

        var box = Bounds;

        return world.X >= box.X - tolerance && world.X <= box.X + box.Width + tolerance &&
               world.Y >= box.Y - tolerance && world.Y <= box.Y + box.Height + tolerance;
    }

    /// <summary>Whether a world rectangle touches the text's box - turned, the rectangle turned back onto it.</summary>
    public bool Touches(Rect world)
    {
        if (!Transform.IsSomething) return CanvasTouch.Meets(Bounds, world);

        var quad = CanvasTouch.Corners(world);
        var pivot = Pivot;
        for (var i = 0; i < quad.Length; i++) quad[i] = Transform.Undo(quad[i], pivot);

        return CanvasTouch.ConvexOverlap(quad, CanvasTouch.Corners(Bounds));
    }

    public void Render(CanvasDrawing drawing)
    {
        if (string.IsNullOrEmpty(Text) || Brush is not SolidColorBrush solid) return;

        // Shaped at the size it is on screen as it is DRAWN - see the note at the top - and scaled back to its size on
        // the plane by its placement, so it is pixel for pixel now and rides the camera after. Below a pixel there is
        // nothing to read, and shaping it would be work thrown away.
        var scale = drawing.Canvas.Scale;
        var onScreen = FontSize * scale;
        if (onScreen < 1) return;

        var size = ShapeForDrawing(onScreen);
        if (size.Width <= 0 || size.Height <= 0) return;

        // On a WHOLE pixel as it is drawn, which is where shaped text reads sharp: the camera moves it by whole pixels
        // after, the way a hand drags it, and keeps it there.
        var screen = drawing.Canvas.WorldToScreen(Origin);
        var snap = new Vector2<double>(Math.Round(screen.X) - screen.X, Math.Round(screen.Y) - screen.Y);
        var at = drawing.Local(Origin) + snap * (1 / scale);
        var back = (float)(1 / scale);
        var placement = drawing.PartWith(Matrix4x4.CreateScale<float>(back, back, 1)
                                         * Matrix4x4.CreateTranslation<float>((float)at.X, (float)at.Y, 0));

        drawing.Session.DrawText(
            new TextRenderingParameters
            {
                HorizontalTextAlignment = HorizontalTextAlignment.Left,
                VerticalTextAlignment = VerticalTextAlignment.Top,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
                Color = solid.Color,
                TextArea = new Rectangle<int>(new Vector2F().As<int>(), size.As<int>())
            },
            size, _draw, Brush, Brushes.Transparent, Brushes.Transparent, placement);
    }

    // Shaped once at a size the text stack can answer for, then scaled: a layout answers in whole pixels and never in
    // less than one, so a world size of 0.014 measured one whole unit square - a frame the size of the sky round a word.
    private const double MeasuringSize = 64;

    private Size<double> MeasureAt(double fontSize)
    {
        if (!EnsureFont()) return default;

        if (_shapedText != Text || Double.IsNaN(_shapedSize))
        {
            _measured = Shape(_measure, MeasuringSize);
            _shapedText = Text;
            _shapedSize = MeasuringSize;
        }

        var times = fontSize / MeasuringSize;

        return new Size<double>(_measured.Width * times, _measured.Height * times);
    }

    // ...and the layout that is actually handed to the drawing, shaped at the size it will appear on screen.
    private Size<double> ShapeForDrawing(double fontSize)
    {
        if (!EnsureFont()) return default;
        if (_drawnText == Text && _drawnSize.Equals(fontSize)) return _drawn;

        _drawn = Shape(_draw, fontSize);
        _drawnText = Text;
        _drawnSize = fontSize;

        return _drawn;
    }

    private Size<double> Shape(TextLayout layout, double fontSize) =>
        layout.ProcessText(Text, fontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top, false);

    // A typeface is fixed per layout, so a different face is a different layout - the same rule TextBlock follows.
    private bool EnsureFont()
    {
        var font = FontFamily ?? UIComponent.DefaultFontFamily;
        if (font == null) return false;

        if (_draw != null && ReferenceEquals(_layoutFont, font)) return true;

        _draw = new TextLayout(font.Typeface, font.Fonts[0]);
        _measure = new TextLayout(font.Typeface, font.Fonts[0]);
        _layoutFont = font;
        _shapedText = null;
        _drawnText = null;

        return true;
    }
}
