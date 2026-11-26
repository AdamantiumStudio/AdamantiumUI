using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Printing;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.UI.Sandbox.Printing;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Printing tab: a sample document of a few pages, printed through the print service - to the system's
/// printers, to a PDF file, or with the sample printers on, to printers that offer different things, so the dialog's
/// rows come and go.</summary>
[ViewModel]
public partial class PrintingViewModel : TabPageViewModel
{
    private readonly IPrintService _printing;
    private readonly SamplePrintPlatform _samples = new();

    // The system's own printers, as the platform registered them; none where it has none.
    private readonly IPrintPlatform _system = SystemPrinting.Platform;

    public PrintingViewModel(IPrintService printing) : base("Printing")
    {
        _printing = printing;
    }

    /// <summary>Whether the system's printers are offered - off leaves the PDF file alone.</summary>
    [Bindable] private bool _systemPrinters = true;

    /// <summary>Whether the made-up printers stand in for the system's.</summary>
    [Bindable] private bool _samplePrinters;

    /// <summary>How many pages the sample has - a number box's value.</summary>
    [Bindable] private double _pages = 6;

    /// <summary>How the last print ended.</summary>
    [Bindable] private string _outcome = string.Empty;

    partial void OnSystemPrintersChanged(bool value) => Offer();

    partial void OnSamplePrintersChanged(bool value) => Offer();

    private void Offer() => SystemPrinting.Platform = SamplePrinters ? _samples : SystemPrinters ? _system : null;

    [Command]
    private async Task Print() => Tell(await _printing.PrintAsync(new SamplePrintDocument((int)System.Math.Max(1, Pages))));

    /// <summary>Prints the element the button names - the sample card, as it shows.</summary>
    [Command]
    private async Task PrintCard(object card)
    {
        if (card is IUIComponent element)
        {
            Tell(await _printing.PrintAsync(new ElementPrintDocument(element, PrintingStrings.CardTitle)));
        }
    }

    private void Tell(PrintResult result)
    {
        Outcome = result switch
        {
            PrintResult.Printed when SamplePrinters => PrintingStrings.Printed + " " + _samples.LastJob,
            PrintResult.Printed => PrintingStrings.PrintedBySystem,
            PrintResult.Saved => PrintingStrings.Saved,
            PrintResult.Failed => PrintingStrings.Failed,
            _ => PrintingStrings.Canceled,
        };
    }
}
