using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.UI.Core;
using Adamantium.Pdf.Shadings;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// Colors (ISO 32000-2, 8.6.8), and the brushes and pens they become: one frozen brush per color and opacity, one pen per
// set of line parameters, however often the page uses them.
internal sealed partial class PdfPageInterpreter
{
    private readonly Dictionary<uint, Brush> _brushes = [];
    private readonly Dictionary<(uint, double, int, int, string, double, double), Pen> _pens = [];
    private readonly Dictionary<(PdfStream, uint), PdfContentDrawing> _cells = [];
    private readonly Dictionary<PdfObject, PdfPattern> _patterns = new(ReferenceEqualityComparer.Instance);

    private bool ColorOperator(PdfContentOperation operation, PdfFrame frame)
    {
        switch (operation.Operator)
        {
            case "cs":
                _state.Fill = PdfPaint.Initial(SpaceOf(operation, frame));
                return true;
            case "CS":
                _state.Stroke = PdfPaint.Initial(SpaceOf(operation, frame));
                return true;
            case "sc":
            case "scn":
                _state.Fill = WithComponents(_state.Fill, operation, frame);
                BeginCell(_state.Fill);
                return true;
            case "SC":
            case "SCN":
                _state.Stroke = WithComponents(_state.Stroke, operation, frame);
                BeginCell(_state.Stroke);
                return true;
            case "g":
                _state.Fill = Device(new PdfDeviceGrayColorSpace(), operation, 1);
                return true;
            case "G":
                _state.Stroke = Device(new PdfDeviceGrayColorSpace(), operation, 1);
                return true;
            case "rg":
                _state.Fill = Device(new PdfDeviceRgbColorSpace(), operation, 3);
                return true;
            case "RG":
                _state.Stroke = Device(new PdfDeviceRgbColorSpace(), operation, 3);
                return true;
            case "k":
                _state.Fill = Device(new PdfDeviceCmykColorSpace(), operation, 4);
                return true;
            case "K":
                _state.Stroke = Device(new PdfDeviceCmykColorSpace(), operation, 4);
                return true;
            default:
                return false;
        }
    }

    private PdfColorSpace SpaceOf(PdfContentOperation operation, PdfFrame frame)
    {
        var space = operation.Operands.Count > 0
            ? PdfColorSpaces.Create(operation.Operands[0], frame.Resources, _document)
            : null;
        if (space == null)
        {
            Tell($"A color space ({(operation.Operands.Count > 0 ? operation.Operands[0] : "none")}) could not be " +
                 "read; DeviceGray stands in.");
            space = new PdfDeviceGrayColorSpace();
        }

        return space;
    }

    // A device space named by its operator: the document's Default space or output intent stands in, as for cs.
    private PdfPaint Device(PdfColorSpace fallback, PdfContentOperation operation, int count)
    {
        var name = count switch
        {
            1 => "DeviceGray",
            3 => "DeviceRGB",
            _ => "DeviceCMYK",
        };
        var space = PdfColorSpaces.Create(new PdfName(name), _frames.Peek().Resources, _document) ?? fallback;
        var components = new double[count];
        for (var i = 0; i < count; i++)
        {
            components[i] = Math.Clamp(operation.Number(i), 0, 1);
        }

        return new PdfPaint(space, components);
    }

    private PdfPaint WithComponents(PdfPaint paint, PdfContentOperation operation, PdfFrame frame)
    {
        var numbers = new List<double>();
        PdfPattern pattern = null;
        foreach (var operand in operation.Operands)
        {
            if (operand is PdfInteger or PdfReal)
            {
                numbers.Add(operand is PdfInteger integer ? integer.Value : ((PdfReal)operand).Value);
            }
            else if (operand is PdfName name && paint.Space is PdfPatternColorSpace)
            {
                pattern = PatternOf(frame.Resources.Pattern(name.Value), frame.Resources);
                if (pattern == null)
                {
                    Tell($"The pattern '{name.Value}' could not be read.");
                }
            }
        }

        if (paint.Space is PdfPatternColorSpace)
        {
            return new PdfPaint(paint.Space, numbers.ToArray(), pattern) { PatternBase = frame.BaseCtm };
        }

        var components = new double[paint.Space.Components];
        for (var i = 0; i < components.Length && i < numbers.Count; i++)
        {
            components[i] = numbers[i];
        }

        return new PdfPaint(paint.Space, components);
    }

    // A pattern is read once however often it is chosen.
    private PdfPattern PatternOf(PdfObject value, Adamantium.Pdf.Resources.PdfResources resources)
    {
        if (value == null)
        {
            return null;
        }

        if (!_patterns.TryGetValue(value, out var pattern))
        {
            pattern = PdfPattern.Create(value, resources, _document);
            _patterns[value] = pattern;
        }

        return pattern;
    }

    // A tiling pattern's cell is walked as soon as the pattern is chosen - drawn later, it would have to stop the
    // painting operator halfway. Once for each pattern and color.
    private void BeginCell(PdfPaint paint)
    {
        if (paint.Pattern is not PdfTilingPattern tiling)
        {
            return;
        }

        var color = tiling.IsColored ? 0u : Pack(paint.Rgb(), 1);
        if (_cells.TryGetValue((tiling.Cell, color), out var known))
        {
            paint.Cell = known;
            return;
        }

        var inside = InitialState(PdfMatrix.Identity);
        if (!tiling.IsColored && paint.Space is PdfPatternColorSpace { Underlying: { } underlying })
        {
            inside.Fill = inside.Stroke = new PdfPaint(underlying, paint.Components);
        }

        var box = tiling.BBox;
        var bounds = new Rect(Math.Min(box.Left, box.Right), Math.Min(box.Bottom, box.Top), Math.Abs(box.Width),
            Math.Abs(box.Height));
        EnterForm(tiling.Cell, tiling.Resources, PdfMatrix.Identity, PdfFrameKind.PatternCell, [], inside, steps =>
        {
            paint.Cell = new PdfContentDrawing(steps, bounds);
            _cells[(tiling.Cell, color)] = paint.Cell;
        }, bounds);
    }

    private Brush FillBrush() => Solid(_state.Fill.Rgb(), _state.FillAlpha);

    private Brush Solid((double R, double G, double B) rgb, double alpha)
    {
        var key = Pack(rgb, alpha);
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(ColorOf(key)).CaptureBase();
            _brushes[key] = brush;
        }

        return brush;
    }

    private Pen StrokePen()
    {
        var key = Pack(_state.Stroke.Rgb(), _state.StrokeAlpha);
        var dash = _state.Dash.IsSolid ? "" : string.Join(" ", _state.Dash.Lengths);
        var phase = _state.Dash.IsSolid ? 0 : _state.Dash.Phase;
        var penKey = (key, _state.LineWidth, _state.LineCap, _state.LineJoin, dash, phase, _state.MiterLimit);
        if (_pens.TryGetValue(penKey, out var pen))
        {
            return pen;
        }

        var cap = _state.LineCap switch
        {
            1 => PenLineCap.ConvexRound,
            2 => PenLineCap.Square,
            _ => PenLineCap.Flat,
        };
        var join = _state.LineJoin switch
        {
            1 => PenLineJoin.Round,
            2 => PenLineJoin.Bevel,
            _ => PenLineJoin.Miter,
        };
        var brush = Solid(_state.Stroke.Rgb(), _state.StrokeAlpha);
        pen = new Pen(brush, _state.LineWidth, phase, _state.Dash.IsSolid ? null : _state.Dash.Lengths, cap, cap, join,
            dashStartCap: cap, dashEndCap: cap)
        {
            // A width of 0 is the thinnest line the device can show (ISO 32000-2, 8.4.3.2).
            MinimumDeviceThickness = _state.LineWidth == 0 ? 1 : 0,
            // The miter's length over the line's width, as the pen counts it: its point's reach over half the width.
            MiterLimit = _state.MiterLimit,
        };
        _pens[penKey] = pen;
        return pen;
    }

    // Each drawn object with a blend mode or a soft mask is put down as a group of its own; so is one whose opacity its
    // own brush cannot carry - an image.
    private bool BeginObjectGroup(List<PdfDrawStep> output, double opacity = 1)
    {
        return (_state.NeedsObjectGroup || opacity < 1) && OpenGroup(ObjectGroup(opacity), output);
    }

    private void EndObjectGroup(bool grouped, List<PdfDrawStep> output)
    {
        if (grouped)
        {
            CloseGroupsDownTo(_openGroups - 1, output);
        }
    }

    private PdfPushGroupStep ObjectGroup(double opacity) => new()
    {
        Mask = _state.SoftMask,
        MaskKind = _state.SoftMaskKind == Adamantium.Pdf.Graphics.PdfSoftMaskKind.Luminosity
            ? MaskKind.Luminosity
            : MaskKind.Alpha,
        MaskBackdrop = ColorOf(Pack(_state.SoftMaskBackdrop, 1)),
        BlendMode = (BlendMode)(int)_state.BlendMode,
        Opacity = opacity,
    };

    private static uint Pack((double R, double G, double B) rgb, double alpha) =>
        (uint)Byte(rgb.R) << 24 | (uint)Byte(rgb.G) << 16 | (uint)Byte(rgb.B) << 8 | Byte(alpha);

    private static Color ColorOf(uint packed) =>
        new((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

    private static byte Byte(double value) =>
        (byte)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1) * 255);
}
