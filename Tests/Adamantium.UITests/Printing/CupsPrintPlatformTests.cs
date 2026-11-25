using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Platforms.Cups;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>Against the system's own CUPS, where there is one with a printer - the stand has a cups-pdf queue and an IPP
/// Everywhere one. Only a printer named in ADAM_TEST_PRINTER is printed on.</summary>
public class CupsPrintPlatformTests
{
    private readonly CupsPrintPlatform _platform = new();

    [Test]
    public async Task WithoutCups_ThereAreNoPrinters_AndNothingThrows()
    {
        Assume.That(Cups.IsAvailable, Is.False);

        Assert.That(await _platform.FindPrintersAsync(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task EachPrinter_SaysWhatItCanDo_AndTakesItsOwnChoices()
    {
        Assume.That(Cups.IsAvailable && !OperatingSystem.IsWindows());
        var printers = await _platform.FindPrintersAsync(CancellationToken.None);
        Assume.That(printers, Is.Not.Empty);

        foreach (var printer in printers)
        {
            var offered = await _platform.CapabilitiesAsync(printer, CancellationToken.None);
            TestContext.Out.WriteLine($"{printer.Id} '{printer.Name}' {printer.State} default={printer.IsDefault}: " +
                                      $"default {offered.DefaultPaper}, " +
                                      $"papers [{string.Join(", ", offered.Papers)}], " +
                                      $"copies {offered.MaxCopies}, duplex [{string.Join(",", offered.Duplex)}], " +
                                      $"colors [{string.Join(",", offered.Colors)}], " +
                                      $"qualities [{string.Join(",", offered.Qualities)}], " +
                                      $"trays [{string.Join(",", offered.Trays)}], types [{string.Join(",", offered.MediaTypes)}]");
            var settings = new PrintSettings { Printer = printer }.FitTo(offered);

            Assert.Multiple(() =>
            {
                Assert.That(offered.Papers, Is.Not.Empty);
                Assert.That(offered.Papers.Select(paper => paper.Name), Is.Unique);
                Assert.That(_platform.Reconcile(offered, settings), Is.EqualTo(settings), "its defaults go together");
            });
        }
    }

    [Test]
    public async Task AJob_IsPrinted()
    {
        Assume.That(Environment.GetEnvironmentVariable("ADAM_TEST_PRINTER"), Is.Not.Null.And.Not.Empty);
        var printers = await _platform.FindPrintersAsync(CancellationToken.None);
        var printer = printers.First(found => found.Id == Environment.GetEnvironmentVariable("ADAM_TEST_PRINTER"));
        var offered = await _platform.CapabilitiesAsync(printer, CancellationToken.None);
        var settings = new PrintSettings
        {
            Printer = printer, Copies = 2, Duplex = DuplexMode.LongEdge, Color = PrintColorMode.Grayscale,
            Tray = offered.Trays.LastOrDefault(), MediaType = offered.MediaTypes.LastOrDefault(),
            Orientation = PageOrientation.Landscape,
        }.FitTo(offered);
        var told = 0;

        await _platform.PrintAsync(new PrintSheets(new XpsProbeDocument(), settings, offered), settings,
            new Counting(() => told++), CancellationToken.None);

        Assert.That(told, Is.GreaterThan(0));
    }

    [Test]
    public async Task AJobCalledOff_IsNotPrinted()
    {
        Assume.That(Environment.GetEnvironmentVariable("ADAM_TEST_PRINTER"), Is.Not.Null.And.Not.Empty);
        var printers = await _platform.FindPrintersAsync(CancellationToken.None);
        var printer = printers.First(found => found.Id == Environment.GetEnvironmentVariable("ADAM_TEST_PRINTER"));
        var offered = await _platform.CapabilitiesAsync(printer, CancellationToken.None);
        var settings = new PrintSettings { Printer = printer }.FitTo(offered);
        using var cancellation = new CancellationTokenSource();

        // Called off once every sheet is drawn and the job is about to be sent.
        Assert.CatchAsync<OperationCanceledException>(() => _platform.PrintAsync(
            new PrintSheets(new XpsProbeDocument(), settings, offered), settings,
            new Counting(cancellation.Cancel), cancellation.Token));
    }

    private sealed class Counting(Action told) : IProgress<PrintProgress>
    {
        public void Report(PrintProgress value) => told();
    }
}
