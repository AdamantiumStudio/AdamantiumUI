using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Pdf.Interpretation;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>A PDF page as a drawing: read once, then drawn at any size and zoom without being read again. Its units are
/// the page's points (times its <c>/UserUnit</c>), y down, from the top left corner of the crop box, the page's
/// <c>/Rotate</c> already applied. What could not be drawn is told in <see cref="Problems"/>; the rest is drawn.
/// <para>Built on any thread, but the pages of one document one at a time - a document is read from one thread at a
/// time.</para></summary>
public sealed class PdfPageDrawing : Drawing
{
    private readonly IReadOnlyList<PdfDrawStep> _steps;
    private readonly IReadOnlyList<PdfPressableSteps> _pressable;
    private readonly PdfPressableSteps _pressed;

    private PdfPageDrawing(IReadOnlyList<PdfDrawStep> steps, Rect bounds, IReadOnlyList<string> problems,
        IReadOnlyList<PdfPressableSteps> pressable, PdfPressableSteps pressed)
    {
        _steps = steps;
        Bounds = bounds;
        Problems = problems;
        _pressable = pressable;
        _pressed = pressed;
    }

    public override Rect Bounds { get; }

    /// <summary>What was not drawn, and why - a stream that could not be read, an operator not known, a limit reached.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    internal IReadOnlyList<PdfDrawStep> Steps => _steps;

    /// <summary>The paths the page's content draws, in the order drawn - what a measurement snaps to; not the covers a
    /// pattern or a mask lays. Made anew each call, so called once a sheet.</summary>
    public IReadOnlyList<PdfDrawnPath> CollectPaths()
    {
        var paths = new List<PdfDrawnPath>();
        foreach (var step in _steps)
        {
            if (step is PdfGeometryStep { IsPagePath: true, Geometry: not null } drawn)
            {
                paths.Add(new PdfDrawnPath(drawn.Geometry, drawn.Matrix, drawn.Stroke != null, drawn.Fill != null));
            }
        }

        return paths;
    }

    public static PdfPageDrawing Build(PdfDocument document, PdfPage page, PdfPageDrawingOptions options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        var interpreter = new PdfPageInterpreter(document, options ?? PdfPageDrawingOptions.Default, cancellation);
        var steps = interpreter.Draw(page);
        return new PdfPageDrawing(steps, new Rect(0, 0, page.Width, page.Height), interpreter.Problems,
            interpreter.Pressable, null);
    }

    /// <summary>Whether <paramref name="annotation"/> was drawn with a down appearance to show while it is pressed.
    /// </summary>
    public bool CanPress(PdfAnnotation annotation) => PressableOf(annotation) != null;

    /// <summary>The page with <paramref name="annotation"/> drawn by its down appearance in place of its normal one;
    /// with none pressed for one with no down appearance, or null.</summary>
    public PdfPageDrawing Pressing(PdfAnnotation annotation)
    {
        var pressable = annotation == null ? null : PressableOf(annotation);
        return ReferenceEquals(pressable, _pressed)
            ? this
            : new PdfPageDrawing(_steps, Bounds, Problems, _pressable, pressable);
    }

    public override void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor)
    {
        if (_pressed == null)
        {
            Draw(session, transform, _steps, 0, _steps.Count);
            return;
        }

        Draw(session, transform, _steps, 0, _pressed.Start);
        Draw(session, transform, _pressed.Down, 0, _pressed.Down.Count);
        Draw(session, transform, _steps, _pressed.End, _steps.Count);
    }

    private PdfPressableSteps PressableOf(PdfAnnotation annotation)
    {
        foreach (var pressable in _pressable)
        {
            if (ReferenceEquals(pressable.Annotation, annotation.Dictionary))
            {
                return pressable;
            }
        }

        return null;
    }

    private static void Draw(IDrawingSession session, Matrix4x4F transform, IReadOnlyList<PdfDrawStep> steps, int from,
        int to)
    {
        for (var index = from; index < to; index++)
        {
            steps[index].Draw(session, transform);
        }
    }
}
