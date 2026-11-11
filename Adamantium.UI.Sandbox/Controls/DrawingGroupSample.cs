using System;
using System.Collections.Generic;
using Adamantium.Core.Shared.Fonts;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Sandbox.Controls;

/// <summary>One group of draws as a PDF page puts them down - cut by a path, a rectangle or letters, let through a soft
/// mask, nested, blended - over a picture of its own: what the renderer's groups (IDrawingSession.PushGroup) do, seen.
/// </summary>
public class DrawingGroupSample : MeasurableUIComponent
{
    public static readonly AdamantiumProperty SampleProperty = AdamantiumProperty.Register(nameof(Sample),
        typeof(DrawingGroupSampleKind), typeof(DrawingGroupSample),
        new PropertyMetadata(DrawingGroupSampleKind.StarClip, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BlendModeProperty = AdamantiumProperty.Register(nameof(BlendMode),
        typeof(BlendMode), typeof(DrawingGroupSample),
        new PropertyMetadata(BlendMode.Normal, PropertyMetadataOptions.AffectsRender));

    private static readonly LinearGradientBrush Sky = new()
    {
        StartPoint = new Vector2<double>(0, 0),
        EndPoint = new Vector2<double>(1, 1),
        GradientStops =
        {
            new GradientStop(new Color(0x1E, 0x3A, 0x8A), 0), new GradientStop(new Color(0x0E, 0xA5, 0xE9), 0.5),
            new GradientStop(new Color(0xFD, 0xE6, 0x8A), 1)
        }
    };

    private GlyphRun _letters;
    private Size<double> _lettersFor;

    public DrawingGroupSampleKind Sample
    {
        get => GetValue<DrawingGroupSampleKind>(SampleProperty);
        set => SetValue(SampleProperty, value);
    }

    /// <summary>How the discs of <see cref="DrawingGroupSampleKind.Blend"/> are mixed with the picture.</summary>
    public BlendMode BlendMode
    {
        get => GetValue<BlendMode>(BlendModeProperty);
        set => SetValue(BlendModeProperty, value);
    }

    protected override void OnRender(IDrawingContext context)
    {
        var session = context.ForControl(this);
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w <= 0 || h <= 0) return;

        switch (Sample)
        {
            case DrawingGroupSampleKind.StarClip:
                Picture(session, w, h, new DrawingGroupOptions { Clip = Star(w, h, FillRule.NonZero) });
                break;
            case DrawingGroupSampleKind.EvenOddClip:
                Picture(session, w, h, new DrawingGroupOptions { Clip = Star(w, h, FillRule.EvenOdd) });
                break;
            case DrawingGroupSampleKind.RectangleClip:
                Picture(session, w, h,
                    new DrawingGroupOptions { Clip = new RectangleGeometry(new Rect(w * 0.2, h * 0.25, w * 0.6, h * 0.5)) });
                break;
            case DrawingGroupSampleKind.TextClip:
                Picture(session, w, h,
                    new DrawingGroupOptions { TextClip = [new PlacedGlyphRun(Letters(w, h), Matrix4x4F.Identity)] });
                break;
            case DrawingGroupSampleKind.LuminosityMask:
                Picture(session, w, h, new DrawingGroupOptions
                {
                    Mask = Spot(w, h), MaskKind = MaskKind.Luminosity, MaskBackdrop = Colors.Black
                });
                break;
            case DrawingGroupSampleKind.AlphaMask:
                Picture(session, w, h, new DrawingGroupOptions { Mask = Fade(w, h), MaskKind = MaskKind.Alpha });
                break;
            case DrawingGroupSampleKind.Nested:
                session.DrawRectangle(Brushes.White, new Rect(0, 0, w, h));
                session.PushGroup(new DrawingGroupOptions { Clip = Star(w, h, FillRule.NonZero) });
                DrawPicture(session, w, h);
                session.PushGroup(new DrawingGroupOptions { Opacity = 0.7, BlendMode = BlendMode.Multiply });
                Discs(session, w, h);
                session.PopGroup();
                session.PopGroup();
                break;
            case DrawingGroupSampleKind.BlendInClip:
                DrawPicture(session, w, h);
                session.PushGroup(new DrawingGroupOptions { Clip = Star(w, h, FillRule.NonZero) });
                session.PushGroup(new DrawingGroupOptions { BlendMode = BlendMode.Multiply });
                Discs(session, w, h);
                session.PopGroup();
                session.PopGroup();
                break;
            case DrawingGroupSampleKind.ColoredMesh:
                session.DrawRectangle(Brushes.White, new Rect(0, 0, w, h));
                session.DrawColoredMesh(Wheel(w, h), Matrix4x4F.Identity);
                break;
            default:
                DrawPicture(session, w, h);
                session.PushGroup(new DrawingGroupOptions { BlendMode = BlendMode });
                Discs(session, w, h);
                session.PopGroup();
                break;
        }
    }

    // Twelve triangles round a white middle, their outer corners the hues of a color wheel.
    private static ColoredMesh Wheel(double w, double h)
    {
        var center = new Vector2<double>(w / 2, h / 2);
        var r = Math.Min(w, h) * 0.45;
        var hues = new[]
        {
            new Color(0xEF, 0x44, 0x44), new Color(0xF9, 0x73, 0x16), new Color(0xEA, 0xB3, 0x08),
            new Color(0x84, 0xCC, 0x16), new Color(0x22, 0xC5, 0x5E), new Color(0x14, 0xB8, 0xA6),
            new Color(0x06, 0xB6, 0xD4), new Color(0x3B, 0x82, 0xF6), new Color(0x63, 0x66, 0xF1),
            new Color(0xA8, 0x55, 0xF7), new Color(0xEC, 0x48, 0x99), new Color(0xF4, 0x3F, 0x5E),
        };
        var vertices = new List<ColoredVertex>();
        for (var i = 0; i < hues.Length; i++)
        {
            var a0 = 2 * Math.PI * i / hues.Length;
            var a1 = 2 * Math.PI * (i + 1) / hues.Length;
            vertices.Add(new ColoredVertex(center, Colors.White));
            vertices.Add(new ColoredVertex(new Vector2<double>(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0)), hues[i]));
            vertices.Add(new ColoredVertex(new Vector2<double>(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1)),
                hues[(i + 1) % hues.Length]));
        }

        return new ColoredMesh(vertices);
    }

    // The picture on white, cut or masked by the group.
    private static void Picture(IDrawingSession session, double w, double h, DrawingGroupOptions options)
    {
        session.DrawRectangle(Brushes.White, new Rect(0, 0, w, h));
        session.PushGroup(options);
        DrawPicture(session, w, h);
        session.PopGroup();
    }

    private static void DrawPicture(IDrawingSession session, double w, double h)
    {
        session.DrawRectangle(Sky, new Rect(0, 0, w, h));
        session.DrawEllipse(new Rect(w * 0.55, h * 0.1, w * 0.3, w * 0.3), new SolidColorBrush(new Color(0xF9, 0x73, 0x16)),
            0, 360, Adamantium.ProceduralGeometry.Shapes.EllipseType.Sector);
        session.DrawRectangle(new SolidColorBrush(new Color(0x16, 0xA3, 0x4A)), new Rect(0, h * 0.72, w, h * 0.28));
    }

    private static void Discs(IDrawingSession session, double w, double h)
    {
        var r = Math.Min(w, h) * 0.32;
        var cx = w / 2;
        var cy = h / 2;
        foreach (var (dx, dy, color) in new[]
                 {
                     (-0.45, -0.3, new Color(0xEF, 0x44, 0x44)), (0.45, -0.3, new Color(0x22, 0xC5, 0x5E)),
                     (0.0, 0.4, new Color(0x3B, 0x82, 0xF6))
                 })
        {
            session.DrawEllipse(new Rect(cx + dx * r - r, cy + dy * r - r, 2 * r, 2 * r), new SolidColorBrush(color), 0, 360,
                Adamantium.ProceduralGeometry.Shapes.EllipseType.Sector);
        }
    }

    // A five-pointed star drawn in one stroke, its points joined across: by nonzero the whole star, by even-odd its
    // middle left out.
    private static Geometry Star(double w, double h, FillRule rule)
    {
        var geometry = new StreamGeometry { FillRule = rule };
        var context = geometry.Open();
        var r = Math.Min(w, h) * 0.48;
        var points = new List<Vector2<double>>();
        for (var i = 0; i < 5; i++)
        {
            var angle = -Math.PI / 2 + i * 4 * Math.PI / 5;
            points.Add(new Vector2<double>(w / 2 + r * Math.Cos(angle), h / 2 + r * Math.Sin(angle)));
        }

        var figure = context.BeginFigure(points[0], true, true);
        for (var i = 1; i < points.Count; i++)
        {
            figure.LineTo(points[i]);
        }

        figure.CloseFigure();
        geometry.RecalculateBounds();
        return geometry;
    }

    private static Drawing Spot(double w, double h) => new GeometryDrawing
    {
        Brush = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.Black, 1) }
        },
        Geometry = new EllipseGeometry(new Rect(0, 0, w, h))
    };

    private static Drawing Fade(double w, double h) => new GeometryDrawing
    {
        Brush = new LinearGradientBrush
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(1, 0),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, 1) }
        },
        Geometry = new RectangleGeometry(new Rect(0, 0, w, h))
    };

    // "PDF" in bold, set by hand - each glyph at the pen, the pen moved by its advance - and fitted into the box.
    private GlyphRun Letters(double w, double h)
    {
        var size = new Size<double>(w, h);
        if (_letters != null && _lettersFor == size) return _letters;

        var font = Theme.SystemDefaultFontFamily.GetFont(FontWeight.Black, FontStyle.Normal, FontStretch.Normal);
        var emSize = h * 0.62;
        var scale = emSize / font.UnitsPerEm;
        var pen = 0.0;
        var glyphs = new List<PositionedGlyph>();
        foreach (var symbol in "PDF")
        {
            var glyph = font.GetGlyphByCharacter(symbol);
            glyphs.Add(new PositionedGlyph(glyph.Index, pen, 0));
            pen += font.GetAdvanceWidth(glyph.Index) * scale;
        }

        var left = (w - pen) / 2;
        var baseline = h / 2 + emSize * 0.35;
        for (var i = 0; i < glyphs.Count; i++)
        {
            glyphs[i] = glyphs[i] with { X = glyphs[i].X + left, Y = baseline };
        }

        _letters = new GlyphRun(font, emSize, glyphs);
        _lettersFor = size;
        return _letters;
    }
}
