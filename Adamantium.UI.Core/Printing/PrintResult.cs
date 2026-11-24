namespace Adamantium.UI.Core.Printing;

/// <summary>How a print ended.</summary>
public enum PrintResult
{
    /// <summary>Left without printing - in the dialog, or when asked where to save.</summary>
    Canceled,

    /// <summary>Sent to the printer.</summary>
    Printed,

    /// <summary>Written to a PDF file.</summary>
    Saved,
}
