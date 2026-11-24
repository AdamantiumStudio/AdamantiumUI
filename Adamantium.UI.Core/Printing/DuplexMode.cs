namespace Adamantium.UI.Core.Printing;

/// <summary>Which sides of the sheet are printed, and which way the second turns.</summary>
public enum DuplexMode
{
    /// <summary>One side only.</summary>
    OneSided,

    /// <summary>Both sides, turned over the long edge - a book's way, for a portrait sheet.</summary>
    LongEdge,

    /// <summary>Both sides, turned over the short edge - a notepad's way.</summary>
    ShortEdge,
}
