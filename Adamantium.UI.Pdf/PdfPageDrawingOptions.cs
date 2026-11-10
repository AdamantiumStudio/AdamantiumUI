using System;
using Adamantium.Pdf.Annotations;

namespace Adamantium.UI.Pdf;

/// <summary>What a page is drawn with and for.</summary>
public sealed class PdfPageDrawingOptions
{
    public static PdfPageDrawingOptions Default { get; } = new();

    /// <summary>Whether the page's annotations are drawn, each by its normal appearance.</summary>
    public bool Annotations { get; init; } = true;

    /// <summary>Annotations not to draw, though they are shown - those the caller draws itself.</summary>
    public Func<PdfAnnotation, bool> SkipAnnotation { get; init; }

    /// <summary>Whether the page is drawn for print: optional content and annotations as they are printed, not as they
    /// are shown.</summary>
    public bool Print { get; init; }
}
