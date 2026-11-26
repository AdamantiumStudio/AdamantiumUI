using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Printing;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>Saving to a PDF file: the name offered, and a place that will not take the file.</summary>
public class PrintServiceTests
{
    private sealed class Blank : IPrintDocument
    {
        public string Title => "Blank";

        public int Paginate(PrintPage page) => 1;

        public void DrawPage(int index, IDrawingSession session)
        {
        }
    }

    /// <summary>A dialog left with Print, on the printer given.</summary>
    private sealed class PrintPressed(Printer printer) : IDialogService
    {
        public Task<IDialogResult> ShowDialogAsync(Type dialogViewModelType, NavigationParameters parameters = null,
            DialogHostKind host = DialogHostKind.Default, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDialogResult>(DialogResult.Ok(new NavigationParameters()
                .Add(PrintDialogViewModel.SettingsKey, new PrintSettings { Printer = printer })));

        public Task<IDialogResult> ShowDialogAsync<TDialogViewModel>(NavigationParameters parameters = null,
            DialogHostKind host = DialogHostKind.Default, CancellationToken cancellationToken = default) =>
            ShowDialogAsync(typeof(TDialogViewModel), parameters, host, cancellationToken);
    }

    /// <summary>A printer the system cannot reach.</summary>
    private sealed class Unreachable : IPrintPlatform
    {
        public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<Printer>>([]);

        public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation) =>
            Task.FromResult(new PrinterCapabilities());

        public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings) => settings;

        public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings) => null;

        public Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
            CancellationToken cancellation) => throw new COMException("The printer is offline.", unchecked((int)0x80070006));
    }

    [Test]
    public async Task APrinterThatWillNotTakeTheJob_IsAFailure_NotAnException()
    {
        var before = SystemPrinting.Platform;
        SystemPrinting.Platform = new Unreachable();
        try
        {
            var service = new PrintService(new PrintPressed(new Printer("gone", "Gone")));

            Assert.That(await service.PrintAsync(new Blank()), Is.EqualTo(PrintResult.Failed));
        }
        finally
        {
            SystemPrinting.Platform = before;
        }
    }

    [TestCase("Report v1.2", "Report v1.2.pdf")]
    [TestCase("notes.txt", "notes.txt.pdf")]
    [TestCase("Plan.PDF", "Plan.PDF")]
    [TestCase("a/b", "a_b.pdf")]
    [TestCase("  ", "Document.pdf")]
    public void TheFileOffered_IsTheWholeTitle_ThenPdf(string title, string expected) =>
        Assert.That(PrintService.PdfFileNameOf(title), Is.EqualTo(expected));

    [Test]
    public async Task APlaceThatWillNotTakeTheFile_IsAFailure_AndLeavesNothingBehind()
    {
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var sheets = new PrintSheets(new Blank(), new PrintSettings(), PrinterCapabilities.Pdf);
            sheets.Paginate(sheets.Sheet);

            // A folder of that name stands where the file would go: the move over it fails.
            var path = Path.Combine(folder, "taken.pdf");
            Directory.CreateDirectory(path);

            Assert.That(await PrintService.WritePdfAsync(sheets, path, CancellationToken.None), Is.False);
            Assert.That(File.Exists(path + ".writing"), Is.False, "no half of a PDF left");

            var free = Path.Combine(folder, "free.pdf");
            Assert.That(await PrintService.WritePdfAsync(sheets, free, CancellationToken.None), Is.True);
            Assert.That(new FileInfo(free).Length, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
