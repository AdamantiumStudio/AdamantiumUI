using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.UI.Sandbox.Printing;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Printing tab: a sample document of a few pages, printed through the print service - to a PDF file, or with
/// the sample printers on, to printers that offer different things, so the dialog's rows come and go.</summary>
[ViewModel]
public partial class PrintingViewModel : TabPageViewModel
{
    private readonly IPrintService _printing;
    private readonly SamplePrintPlatform _samples = new();

    public PrintingViewModel(IPrintService printing) : base("Printing")
    {
        _printing = printing;
    }

    /// <summary>Whether the made-up printers stand in for the system's.</summary>
    [Bindable] private bool _samplePrinters;

    /// <summary>How many pages the sample has - a number box's value.</summary>
    [Bindable] private double _pages = 6;

    /// <summary>How the last print ended.</summary>
    [Bindable] private string _outcome = string.Empty;

    partial void OnSamplePrintersChanged(bool value) => SystemPrinting.Platform = value ? _samples : null;

    [Command]
    private async Task Print()
    {
        var result = await _printing.PrintAsync(new SamplePrintDocument((int)System.Math.Max(1, Pages)));
        Outcome = result switch
        {
            PrintResult.Printed => PrintingStrings.Printed + " " + _samples.LastJob,
            PrintResult.Saved => PrintingStrings.Saved,
            _ => PrintingStrings.Canceled,
        };
    }
}
