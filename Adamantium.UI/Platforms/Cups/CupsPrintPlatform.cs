using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Pdf;

namespace Adamantium.UI.Platforms.Cups;

/// <summary>The printers of Linux and macOS, through CUPS: what each can do from its IPP attributes, choices put right by
/// its constraints, and jobs sent as PDF.</summary>
internal sealed class CupsPrintPlatform : IPrintPlatform
{
    private readonly Lock _gate = new();

    // Printers described so far, by name, kept open: the dialog asks on every change whether its choices go together.
    private readonly Dictionary<string, Described> _described = new();

    private sealed record Described(CupsPrinter Printer, CupsChoices Choices);

    public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
        Task.Run(() => Cups.IsAvailable ? CupsPrinter.Find() : (IReadOnlyList<Printer>)[], cancellation)
            .WaitAsync(cancellation);

    public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.IsFile
            ? Task.FromResult(PrinterCapabilities.Pdf)
            : Task.Run(() => Describe(printer.Id).Capabilities, cancellation).WaitAsync(cancellation);
    }

    public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Printer == null || settings.Printer.IsFile)
        {
            return settings;
        }

        lock (_gate)
        {
            return _described.TryGetValue(settings.Printer.Id, out var described) &&
                   described.Printer.PutRight(described.Choices.WriteToCheck(settings)) is { } right
                ? described.Choices.ReadBack(right, settings)
                : settings;
        }
    }

    /// <summary>Never asked: a CUPS driver has no window of its own.</summary>
    public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings) => null;

    public async Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(settings);
        CupsChoices choices;
        lock (_gate)
        {
            choices = _described.GetValueOrDefault(settings.Printer.Id)?.Choices;
        }

        choices ??= Describe(settings.Printer.Id);

        // Drawn on the thread printing was asked from, as a document draws on screen; sent from another, as the server
        // may be slow.
        var laid = sheets as PrintSheets ?? new PrintSheets(sheets, settings, choices.Capabilities);
        using var pdf = new MemoryStream();
        await PdfPrintFile.SaveAsync(new Watched(laid, progress), laid.Sheet, pdf, cancellation);
        var data = pdf.GetBuffer().AsMemory(0, (int)pdf.Length);
        var options = choices.Write(settings);

        // Called off while the server is slow to answer, it is let go of at once; the job is canceled as it answers.
        await Task.Run(() =>
        {
            using var printer = CupsPrinter.Open(settings.Printer.Id);
            printer.Print(string.IsNullOrWhiteSpace(laid.Title) ? "Document" : laid.Title, data.Span, options, cancellation);
        }, cancellation).WaitAsync(cancellation);
    }

    private CupsChoices Describe(string name)
    {
        if (!Cups.IsAvailable)
        {
            throw new IOException("There is no CUPS here.");
        }

        var printer = CupsPrinter.Open(name);
        CupsChoices choices;
        try
        {
            choices = new CupsChoices(printer.Ask());
        }
        catch
        {
            printer.Dispose();
            throw;
        }

        lock (_gate)
        {
            if (_described.Remove(name, out var before))
            {
                before.Printer.Dispose();
            }

            _described[name] = new Described(printer, choices);
        }

        return choices;
    }

    /// <summary>The sheets, each told of as it is drawn.</summary>
    private sealed class Watched(IPrintDocument sheets, IProgress<PrintProgress> progress) : IPrintDocument
    {
        private int _count;

        public string Title => sheets.Title;

        public int Paginate(PrintPage page) => _count = sheets.Paginate(page);

        public Task PrepareAsync(PrintPage page, CancellationToken cancellation) =>
            sheets.PrepareAsync(page, cancellation);

        public Task PreparePagesAsync(IReadOnlyList<int> pages, PrintPage page, CancellationToken cancellation) =>
            sheets.PreparePagesAsync(pages, page, cancellation);

        public bool HasOwnMargins => sheets.HasOwnMargins;

        public void DrawPage(int index, IDrawingSession session)
        {
            sheets.DrawPage(index, session);
            progress?.Report(new PrintProgress(index + 1, _count));
        }
    }
}
