using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Core.Printing;

/// <summary>The system's printers, as the print dialog asks for them: which there are, what each can do, which choices
/// go together, and the job itself. Windows answers from its spooler and print tickets, Linux and macOS from CUPS.
/// </summary>
public interface IPrintPlatform
{
    /// <summary>The printers the system knows, the default among them.</summary>
    Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation);

    /// <summary>What <paramref name="printer"/> can do.</summary>
    Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation);

    /// <summary><paramref name="settings"/> as the system takes them: choices that cannot go together - heavy paper and
    /// both sides - put right as the system puts them. The dialog shows what comes back.</summary>
    PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings);

    /// <summary>Prints <paramref name="sheets"/> - laid out already - on <see cref="PrintSettings.Printer"/>.</summary>
    Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
        CancellationToken cancellation);
}
