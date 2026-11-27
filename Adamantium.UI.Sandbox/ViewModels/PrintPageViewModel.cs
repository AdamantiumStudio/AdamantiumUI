using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Sandbox.Printing;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Print page tab: the print dialog shown inside the window, as an editor shows it - "Print" heads its options,
/// and what it is set to is printed through the print service without another dialog.</summary>
[ViewModel]
public partial class PrintPageViewModel : TabPageViewModel
{
    private readonly IPrintService _printing;

    public PrintPageViewModel(IPrintService printing) : base("PrintPage")
    {
        _printing = printing;
        Platform = printing.Platform;
    }

    /// <summary>The sample printed: six pages.</summary>
    public IPrintDocument Document { get; } = new SamplePrintDocument(6);

    /// <summary>The system's printers, as the print service offers them.</summary>
    public IPrintPlatform Platform { get; }

    /// <summary>Prints as the page was set.</summary>
    [Command]
    private async Task Print(object request)
    {
        if (request is PrintRequest asked)
        {
            await _printing.PrintAsync(asked);
        }
    }
}
