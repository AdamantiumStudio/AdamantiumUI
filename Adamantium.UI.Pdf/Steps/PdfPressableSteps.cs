using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>An annotation with a down appearance, as a page was drawn: where its normal appearance is among the page's
/// draws, and the draws of its down appearance that take their place while it is pressed.</summary>
internal sealed class PdfPressableSteps(PdfDictionary annotation, int start, int end, IReadOnlyList<PdfDrawStep> down)
{
    public PdfDictionary Annotation { get; } = annotation;

    public int Start { get; } = start;

    public int End { get; } = end;

    public IReadOnlyList<PdfDrawStep> Down { get; } = down;
}
