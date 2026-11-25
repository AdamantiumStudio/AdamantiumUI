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

    /// <summary>Not printed: the printer would not take the job - gone off the network, its driver failed - or the PDF
    /// file could not be written - the place is read-only, full, or the file is held open; nothing of it is left behind.
    /// </summary>
    Failed,
}
