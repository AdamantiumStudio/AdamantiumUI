using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Core.Printing;

/// <summary>Printing, for a view model: shows the print dialog over the window in front, then prints what was chosen -
/// on a printer, or into a PDF file the user names. A print page shown inside a window asks with its own options and
/// hands them over as a <see cref="PrintRequest"/>.</summary>
public interface IPrintService
{
    /// <summary>The system's printers, for a print page shown inside a window; null where the system has none to offer
    /// - only the PDF file is then.</summary>
    IPrintPlatform Platform { get; }

    /// <summary>Asks how to print <paramref name="document"/>, showing its <paramref name="currentPage"/> (from 0) as
    /// the current one, and prints it so; says how it ended - canceling is not an error.</summary>
    Task<PrintResult> PrintAsync(IPrintDocument document, int currentPage = 0,
        CancellationToken cancellation = default);

    /// <summary>Prints as <paramref name="request"/> says, without asking - on its printer, or into a PDF file the user
    /// names.</summary>
    Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken cancellation = default);
}
