using System;
using System.Collections.Generic;
using System.Threading;
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

    private PdfPageDrawing(IReadOnlyList<PdfDrawStep> steps, Rect bounds, IReadOnlyList<string> problems)
    {
        _steps = steps;
        Bounds = bounds;
        Problems = problems;
    }

    public override Rect Bounds { get; }

    /// <summary>What was not drawn, and why - a stream that could not be read, an operator not known, a limit reached.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    internal IReadOnlyList<PdfDrawStep> Steps => _steps;

    public static PdfPageDrawing Build(PdfDocument document, PdfPage page, PdfPageDrawingOptions options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        var interpreter = new PdfPageInterpreter(document, options ?? PdfPageDrawingOptions.Default, cancellation);
        var steps = interpreter.Draw(page);
        return new PdfPageDrawing(steps, new Rect(0, 0, page.Width, page.Height), interpreter.Problems);
    }

    public override void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor)
    {
        foreach (var step in _steps)
        {
            step.Draw(session, transform);
        }
    }
}
