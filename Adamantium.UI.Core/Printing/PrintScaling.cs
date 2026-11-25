namespace Adamantium.UI.Core.Printing;

/// <summary>How a page is sized onto its sheet.</summary>
public enum PrintScaling
{
    /// <summary>Shrunk or grown to fill what the printer can reach, keeping its proportions.</summary>
    FitToSheet,

    /// <summary>At its own size, as large as on the paper it was laid out for.</summary>
    ActualSize,

    /// <summary>By <see cref="PrintSettings.ScalePercent"/>.</summary>
    Custom,
}
