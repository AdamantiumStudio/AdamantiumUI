using System;
using System.Collections.Generic;
using Adamantium.Imaging;
using Adamantium.Pdf;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// External objects (ISO 32000-2, 8.8 to 8.10): forms walked as frames of their own, images decoded once each, soft
// masks drawn into pictures of their own, annotations' appearances after the page.
internal sealed partial class PdfPageInterpreter
{
    // An image's unit square, its first row at the top: y = 1 in image space.
    private static readonly PdfMatrix ImageRows = new(1, 0, 0, -1, 0, 1);

    private const int MaxStencilColors = 8;

    private readonly Dictionary<PdfStream, PdfImageBitmap> _decoded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, ImageSource> _images = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, ImageSource> _stencilMasks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PdfStream, uint), ImageSource> _stencils = [];

    private bool ObjectOperator(PdfContentOperation operation, PdfFrame frame)
    {
        switch (operation.Operator)
        {
            case "Do":
                if (Name(operation, 0) is { } name)
                {
                    DrawObject(name, frame);
                }

                return true;
            case "BI":
                if (operation.InlineImage != null && !Hidden)
                {
                    DrawImage(() => PdfImageDecoder.Decode(operation.InlineImage, frame.Resources, _document), null,
                        frame);
                }

                return true;
            case "sh":
                if (Name(operation, 0) is { } shading && !Hidden)
                {
                    PaintShading(shading, frame);
                }

                return true;
            default:
                return false;
        }
    }

    private void DrawObject(string name, PdfFrame frame)
    {
        if (Hidden || frame.Resources.XObject(name) is not { } stream ||
            !_optionalContent.IsVisible(stream.Dictionary["OC"]))
        {
            return;
        }

        switch ((_document.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value)
        {
            case "Image":
                DrawImage(() => PdfImageDecoder.Decode(stream, frame.Resources, _document), stream, frame);
                break;
            case "Form":
                var resources = _document.Resolve(stream.Dictionary["Resources"]) is PdfDictionary own
                    ? new PdfResources(own, _document)
                    : frame.Resources;
                EnterForm(stream, resources, FormMatrix(stream).Then(_state.Ctm), PdfFrameKind.Form, frame.Output,
                    _state.Copy());
                break;
        }
    }

    private PdfMatrix FormMatrix(PdfStream form) =>
        PdfMatrix.From(Numbers(form.Dictionary["Matrix"]));

    /// <summary>Enters a form: its stream walked with <paramref name="inside"/> as the state, placed by
    /// <paramref name="ctm"/>, cut to its box; a form with a transparency group - or any form drawn while a blend mode
    /// or soft mask is set - is put down as one group.</summary>
    private bool EnterForm(PdfStream form, PdfResources resources, PdfMatrix ctm, PdfFrameKind kind,
        List<PdfDrawStep> output, PdfGraphicsState inside, Action<List<PdfDrawStep>> finish = null, Rect? area = null)
    {
        if (!CanEnter(form, kind) || StreamData(form) is not { } data)
        {
            return false;
        }

        _states.Push(_state.Copy());
        inside.Ctm = ctm;
        var frame = new PdfFrame(kind, PdfContentReader.Read(data).GetEnumerator(), resources, output, ctm,
            _states.Count, _openGroups, form) { Finish = finish, Area = area ?? Area };
        var groups = _openGroups;
        if (kind == PdfFrameKind.Form && PdfTransparencyGroup.Of(form, resources, _document) is { } group)
        {
            // The group is one object: what the state sets for objects acts on it as a whole, and inside it starts
            // over (ISO 32000-2, 11.6.6).
            if (group.Knockout)
            {
                Tell("A knockout group is drawn as an ordinary one.");
            }

            OpenGroup(ObjectGroup(_state.FillAlpha), output);
            inside.FillAlpha = inside.StrokeAlpha = 1;
            inside.BlendMode = PdfBlendMode.Normal;
            inside.SoftMask = null;
        }

        // A Type 3 glyph's stream is not a form: it has no box to be cut to.
        if (kind != PdfFrameKind.Glyph && PdfRectangle.From(form.Dictionary["BBox"], _document) is { } box)
        {
            OpenGroup(new PdfPushGroupStep
            {
                Clip = new RectangleGeometry(new Rect(Math.Min(box.Left, box.Right), Math.Min(box.Top, box.Bottom),
                    Math.Abs(box.Width), Math.Abs(box.Height))),
                ClipMatrix = PdfMatrices.ToDrawing(ctm),
            }, output);
        }

        frame.OwnGroups = _openGroups - groups;
        inside.OpenGroups = _openGroups;
        _state = inside;
        Enter(frame);
        return true;
    }

    // An image is decoded once - or found not to decode once - however often it is drawn; an inline image each time
    // it is met.
    private void DrawImage(Func<PdfImageBitmap> decode, PdfStream stream, PdfFrame frame)
    {
        PdfImageBitmap bitmap;
        if (stream != null && _decoded.TryGetValue(stream, out var known))
        {
            bitmap = known;
        }
        else
        {
            try
            {
                bitmap = decode();
            }
            catch (Exception exception) when (exception is PdfException or InvalidOperationException or
                                                  ArgumentException or OverflowException)
            {
                Tell("An image could not be decoded: " + exception.Message);
                bitmap = null;
            }

            if (stream != null)
            {
                _decoded[stream] = bitmap;
            }
        }

        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return;
        }

        var matrix = ImageRows.Then(_state.Ctm);
        if (bitmap.IsStencil)
        {
            DrawStencil(bitmap, stream, matrix, frame);
            return;
        }

        if (stream == null || !_images.TryGetValue(stream, out var image))
        {
            image = ToImage(bitmap.Width, bitmap.Height, bitmap.Rgba);
            if (stream != null)
            {
                _images[stream] = image;
            }
        }

        // An image's opacity is the fill's (ISO 32000-2, 11.6.4.4); its own pixels carry none of it.
        var grouped = BeginObjectGroup(frame.Output, _state.FillAlpha);
        Write(new PdfImageStep(image, PdfMatrices.ToDrawing(matrix)), frame.Output);
        EndObjectGroup(grouped, frame.Output);
    }

    // A stencil mask paints the fill where it is set. In a color, it is a picture of that color - kept for the first
    // MaxStencilColors colors of a stencil; past them, and for a pattern, the stencil is the mask of a group the fill
    // is painted through, one picture whatever the color.
    private void DrawStencil(PdfImageBitmap bitmap, PdfStream stream, PdfMatrix matrix, PdfFrame frame)
    {
        var drawing = PdfMatrices.ToDrawing(matrix);
        var color = Pack(_state.Fill.Rgb(), _state.FillAlpha);
        ImageSource tinted = null;
        if (_state.Fill.Pattern == null && (stream == null || !_stencils.TryGetValue((stream, color), out tinted)) &&
            (stream == null || StencilColors(stream) < MaxStencilColors))
        {
            tinted = Tint(bitmap, color);
            if (stream != null)
            {
                _stencils[(stream, color)] = tinted;
            }
        }

        var grouped = BeginObjectGroup(frame.Output);
        if (tinted != null)
        {
            Write(new PdfImageStep(tinted, drawing), frame.Output);
        }
        else
        {
            if (stream == null || !_stencilMasks.TryGetValue(stream, out var shape))
            {
                shape = ToImage(bitmap.Width, bitmap.Height, bitmap.Rgba);
                if (stream != null)
                {
                    _stencilMasks[stream] = shape;
                }
            }

            var unit = new RectangleGeometry(new Rect(0, 0, 1, 1));
            var mask = new PdfContentDrawing([new PdfImageStep(shape, drawing)],
                PageBounds(new PdfRectangle(0, 0, 1, 1), matrix));
            if (OpenGroup(new PdfPushGroupStep { Mask = mask, MaskKind = MaskKind.Alpha }, frame.Output))
            {
                if (_state.Fill.Pattern != null)
                {
                    PaintPattern(_state.Fill, unit, drawing, frame, stroking: false);
                }
                else
                {
                    Write(new PdfGeometryStep(unit, FillBrush(), null, drawing), frame.Output);
                }

                CloseGroupsDownTo(_openGroups - 1, frame.Output);
            }
        }

        EndObjectGroup(grouped, frame.Output);
    }

    private int StencilColors(PdfStream stream)
    {
        var count = 0;
        foreach (var key in _stencils.Keys)
        {
            if (ReferenceEquals(key.Item1, stream))
            {
                count++;
            }
        }

        return count;
    }

    private static ImageSource Tint(PdfImageBitmap bitmap, uint color)
    {
        var rgba = new byte[bitmap.Rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)(color >> 24);
            rgba[i + 1] = (byte)(color >> 16);
            rgba[i + 2] = (byte)(color >> 8);
            rgba[i + 3] = (byte)(bitmap.Rgba[i + 3] * (color & 0xFF) / 255);
        }

        return ToImage(bitmap.Width, bitmap.Height, rgba);
    }

    private static ImageSource ToImage(int width, int height, byte[] rgba) =>
        width <= 0 || height <= 0
            ? null
            : new BitmapImage((uint)width, (uint)height, 1, 1, SurfaceFormat.R8G8B8A8.UNorm, rgba);

    // A soft mask is drawn when gs sets it, in the matrix in force then; the state it is set in gets the picture.
    private void BeginSoftMask(PdfSoftMask mask, PdfFrame frame)
    {
        var resources = _document.Resolve(mask.Group.Dictionary["Resources"]) is PdfDictionary own
            ? new PdfResources(own, _document)
            : frame.Resources;
        var kind = mask.Kind;
        var backdrop = (0.0, 0.0, 0.0);
        if (kind == PdfSoftMaskKind.Luminosity && mask.Backdrop != null && mask.Transparency?.ColorSpace is { } space &&
            mask.Backdrop.Length == space.Components)
        {
            Span<double> rgb = stackalloc double[3];
            space.ToRgb(mask.Backdrop, rgb);
            backdrop = (rgb[0], rgb[1], rgb[2]);
        }

        if (mask.Transfer != null)
        {
            Tell("A soft mask's transfer function is not applied.");
        }

        var ctm = FormMatrix(mask.Group).Then(_state.Ctm);
        var box = PdfRectangle.From(mask.Group.Dictionary["BBox"], _document);
        EnterForm(mask.Group, resources, ctm, PdfFrameKind.SoftMask, [], InitialState(ctm), steps =>
        {
            _state.SoftMask = new PdfContentDrawing(steps, box is { } b ? PageBounds(b, ctm) : Rect.Empty);
            _state.SoftMaskKind = kind;
            _state.SoftMaskBackdrop = backdrop;
        });
    }

    private static Rect PageBounds(PdfRectangle box, PdfMatrix ctm)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (x, y) in new[] { (box.Left, box.Bottom), (box.Right, box.Bottom), (box.Right, box.Top), (box.Left, box.Top) })
        {
            var point = ctm.Transform(x, y);
            (left, right) = (Math.Min(left, point.X), Math.Max(right, point.X));
            (top, bottom) = (Math.Min(top, point.Y), Math.Max(bottom, point.Y));
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private void DrawAnnotations(PdfPage page, PdfMatrix pageMatrix, List<PdfDrawStep> output)
    {
        IReadOnlyList<PdfAnnotation> annotations;
        try
        {
            annotations = page.Annotations;
        }
        catch (PdfException exception)
        {
            Tell("The page's annotations could not be read: " + exception.Message);
            return;
        }

        foreach (var annotation in annotations)
        {
            // Hidden 2, Print 4, NoView 32 (ISO 32000-2, 12.5.3).
            var shown = _options.Print
                ? (annotation.Flags & 4) != 0 && (annotation.Flags & 2) == 0
                : (annotation.Flags & (2 | 32)) == 0;
            if (!shown || _options.SkipAnnotation?.Invoke(annotation) == true || annotation.Appearance is not { } form ||
                !_optionalContent.IsVisible(annotation.Dictionary["OC"]))
            {
                continue;
            }

            _state = InitialState(pageMatrix);
            var resources = _document.Resolve(form.Dictionary["Resources"]) is PdfDictionary own
                ? new PdfResources(own, _document)
                : new PdfResources(page.Resources, _document);
            if (EnterForm(form, resources, annotation.AppearanceTransform(form).Then(pageMatrix), PdfFrameKind.Form,
                    output, _state.Copy()))
            {
                Walk();
            }
        }
    }
}
