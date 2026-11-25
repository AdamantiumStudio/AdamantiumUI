namespace Adamantium.UI.Core.Printing;

/// <summary>Where a document is printed: a printer the system knows, or - <see cref="Pdf"/> - a PDF file.</summary>
public sealed class Printer
{
    public Printer(string id, string name, bool isDefault = false, PrinterState state = PrinterState.Ready,
        int queuedJobs = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Name = string.IsNullOrEmpty(name) ? id : name;
        IsDefault = isDefault;
        State = state;
        QueuedJobs = Math.Max(0, queuedJobs);
    }

    private Printer()
    {
        Id = PdfId;
        Name = "PDF";
        IsFile = true;
    }

    private const string PdfId = "adamantium:pdf";

    /// <summary>Saving to a PDF file: no printer, so the sheet's options are all the platform's own.</summary>
    public static Printer Pdf { get; } = new();

    /// <summary>What the system calls the printer - its queue's name; stable from one run to the next.</summary>
    public string Id { get; }

    /// <summary>The printer's name as the user knows it.</summary>
    public string Name { get; }

    /// <summary>Whether the system prints to it unless told otherwise.</summary>
    public bool IsDefault { get; }

    /// <summary>Whether it is ready, busy, or held up - as the system says when it is asked.</summary>
    public PrinterState State { get; }

    /// <summary>How many jobs wait in its queue.</summary>
    public int QueuedJobs { get; }

    /// <summary>Whether this is <see cref="Pdf"/> - a file, not a printer.</summary>
    public bool IsFile { get; }

    public override string ToString() => Name;
}
