using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>Where an annotation's normal appearance is among a page's draws, and the down draws replacing it pressed.
/// </summary>
internal sealed class PdfPressableSteps(PdfDictionary annotation, int start, int end, IReadOnlyList<PdfDrawStep> down)
{
    public PdfDictionary Annotation { get; } = annotation;

    public int Start { get; } = start;

    public int End { get; } = end;

    public IReadOnlyList<PdfDrawStep> Down { get; } = down;
}
