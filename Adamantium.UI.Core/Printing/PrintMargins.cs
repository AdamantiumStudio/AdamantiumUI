namespace Adamantium.UI.Core.Printing;

/// <summary>The blank edges of the paper a document is printed inside.</summary>
public enum PrintMargins
{
    /// <summary>The document's own - a text document's sections keep theirs, a PDF's pages are printed as they are.
    /// Offered only for a document that has its own (<see cref="IPrintDocument.HasOwnMargins"/>).</summary>
    Document,

    /// <summary>20 mm on each side.</summary>
    Normal,

    /// <summary>12.7 mm - half an inch - on each side.</summary>
    Narrow,

    /// <summary>25.4 mm - an inch - on each side.</summary>
    Wide,

    /// <summary>None but what the printer cannot reach.</summary>
    None,

    /// <summary>Each side by <see cref="PrintSettings.CustomMargins"/>.</summary>
    Custom,
}
