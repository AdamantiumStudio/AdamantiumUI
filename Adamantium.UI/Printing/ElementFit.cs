namespace Adamantium.UI.Printing;

/// <summary>How a printed element is laid on the sheet.</summary>
public enum ElementFit
{
    /// <summary>Shrunk to the width of what the printer reaches where it is wider; a tall one runs on down the sheets.
    /// </summary>
    Width,

    /// <summary>At its own size; one wider or taller than what the printer reaches runs on across and down the sheets.
    /// </summary>
    None,
}
