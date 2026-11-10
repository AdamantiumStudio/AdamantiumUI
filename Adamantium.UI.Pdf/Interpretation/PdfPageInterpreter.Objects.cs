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

    private readonly Dictionary<PdfStream, ImageSource> _images = new(ReferenceEqualityComparer.Instance);
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
        List<PdfDrawStep> output, PdfGraphicsState inside, Action<List<PdfDrawStep>> finish = null)
    {
        if (!CanEnter(form, kind) || StreamData(form) is not { } data)
        {
            return false;
        }

        _states.Push(_state.Copy());
        inside.Ctm = ctm;
        var frame = new PdfFrame(kind, PdfContentReader.Read(data).GetEnumerator(), resources, output, ctm,
            _states.Count, _openGroups, form) { Finish = finish };
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

        if (PdfRectangle.From(form.Dictionary["BBox"], _document) is { } box)
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

    private void DrawImage(Func<PdfImageBitmap> decode, PdfStream stream, PdfFrame frame)
    {
        ImageSource image;
        PdfImageBitmap bitmap = null;
        if (stream == null || !_images.TryGetValue(stream, out image))
        {
            try
            {
                bitmap = decode();
            }
            catch (Exception exception) when (exception is PdfException or InvalidOperationException or
                                                  ArgumentException or OverflowException)
            {
                Tell("An image could not be decoded: " + exception.Message);
                if (stream != null)
                {
                    _images[stream] = null;
                }

                return;
            }

            image = bitmap.IsStencil ? null : ToImage(bitmap.Width, bitmap.Height, bitmap.Rgba);
            if (stream != null)
            {
                _images[stream] = image;
            }
        }

        if (image == null && (bitmap?.IsStencil ?? IsStencil(stream)))
        {
            image = Stencil(stream, bitmap, decode);
        }

        if (image == null)
        {
            return;
        }

        var grouped = BeginObjectGroup(frame.Output);
        Write(new PdfImageStep(image, PdfMatrices.ToDrawing(ImageRows.Then(_state.Ctm))), frame.Output);
        EndObjectGroup(grouped, frame.Output);
    }

    private bool IsStencil(PdfStream stream) =>
        stream != null && _document.Resolve(stream.Dictionary["ImageMask"]) is PdfBoolean { Value: true };

    // A stencil mask paints the fill color where it is set: one picture of it for each color it is painted in.
    private ImageSource Stencil(PdfStream stream, PdfImageBitmap bitmap, Func<PdfImageBitmap> decode)
    {
        var color = Pack(_state.Fill.Rgb(), _state.FillAlpha);
        if (stream != null && _stencils.TryGetValue((stream, color), out var known))
        {
            return known;
        }

        bitmap ??= decode();
        var rgba = new byte[bitmap.Rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)(color >> 24);
            rgba[i + 1] = (byte)(color >> 16);
            rgba[i + 2] = (byte)(color >> 8);
            rgba[i + 3] = (byte)(bitmap.Rgba[i + 3] * (color & 0xFF) / 255);
        }

        var image = ToImage(bitmap.Width, bitmap.Height, rgba);
        if (stream != null)
        {
            _stencils[(stream, color)] = image;
        }

        return image;
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
