namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>What a content stream being walked draws.</summary>
internal enum PdfFrameKind
{
    /// <summary>The page's contents.</summary>
    Page,

    /// <summary>A form drawn by <c>Do</c>, or an annotation's appearance.</summary>
    Form,

    /// <summary>One glyph of a Type 3 font.</summary>
    Glyph,

    /// <summary>A soft mask's group: drawn apart, into the mask's picture.</summary>
    SoftMask,

    /// <summary>One cell of a tiling pattern: drawn apart, into the pattern's picture.</summary>
    PatternCell,
}
