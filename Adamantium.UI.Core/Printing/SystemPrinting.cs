namespace Adamantium.UI.Core.Printing;

/// <summary>The system's printers, registered once at startup by the platform - Windows from its spooler, Linux and
/// macOS from CUPS. Null where none is written yet: the print dialog then offers the PDF file alone.</summary>
public static class SystemPrinting
{
    public static IPrintPlatform Platform { get; set; }
}
