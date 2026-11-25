using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Core.Printing;

/// <summary>Printing, for a view model: shows the print dialog over the window in front, then prints what was chosen -
/// on a printer, or into a PDF file the user names.</summary>
public interface IPrintService
{
    /// <summary>Asks how to print <paramref name="document"/>, showing its <paramref name="currentPage"/> (from 0) as
    /// the current one, and prints it so; says how it ended - canceling is not an error.</summary>
    Task<PrintResult> PrintAsync(IPrintDocument document, int currentPage = 0,
        CancellationToken cancellation = default);
}
