using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Pdf;
using Adamantium.UI.Themes.Localization;

namespace Adamantium.UI.Printing;

/// <summary>Printing: the print dialog in a window of its own over the window in front, then the sheets as chosen - to
/// the system's printer through <see cref="SystemPrinting.Platform"/>, or into a PDF file the user names.</summary>
internal sealed class PrintService(IDialogService dialogs) : IPrintService
{
    public IPrintPlatform Platform => SystemPrinting.Platform;

    public async Task<PrintResult> PrintAsync(IPrintDocument document, int currentPage = 0,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var parameters = new NavigationParameters()
            .Add(PrintDialogViewModel.DocumentKey, document)
            .Add(PrintDialogViewModel.PlatformKey, Platform)
            .Add(PrintDialogViewModel.CurrentPageKey, currentPage);
        var answer = await dialogs.ShowDialogAsync(typeof(PrintDialogViewModel), parameters, DialogHostKind.Window,
            cancellation);
        if (answer?.Result != DialogButtonResult.Ok ||
            answer.Parameters?.GetValue<PrintSettings>(PrintDialogViewModel.SettingsKey) is not { } settings)
        {
            return PrintResult.Canceled;
        }

        return await PrintAsync(new PrintRequest(document, settings, currentPage), cancellation);
    }

    public async Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.Settings);
        var document = request.Document;
        var settings = request.Settings;
        var currentPage = request.CurrentPage;
        var platform = Platform;
        if (!settings.Printer.IsFile && platform != null)
        {
            try
            {
                var capabilities = await platform.CapabilitiesAsync(settings.Printer, cancellation);
                await platform.PrintAsync(new PrintSheets(document, settings, capabilities, currentPage), settings,
                    Unwatched.Instance, cancellation);
                return PrintResult.Printed;
            }
            catch (OperationCanceledException)
            {
                return PrintResult.Canceled;
            }
            catch (Exception exception) when (exception is ExternalException or IOException)
            {
                // The system would not take the job - a printer gone off the network, a driver that failed.
                return PrintResult.Failed;
            }
        }

        var sheets = new PrintSheets(document, settings, PrinterCapabilities.Pdf, currentPage);

        var path = FileDialog.Save(new SaveFileRequest
        {
            Title = PrintStrings.SaveAsPdf,
            FileName = PdfFileNameOf(document.Title),
            DefaultExtension = "pdf",
            FileTypes = [new FileType("PDF", "pdf")],
            Key = "print-to-pdf",
        });
        if (string.IsNullOrEmpty(path))
        {
            return PrintResult.Canceled;
        }

        // On the thread the dialog was asked from: a document draws its pages there, as it does on screen.
        try
        {
            return await WritePdfAsync(sheets, path, cancellation) ? PrintResult.Saved : PrintResult.Failed;
        }
        catch (OperationCanceledException)
        {
            // Called off while its pictures were made or its pages written: nothing is left behind.
            return PrintResult.Canceled;
        }
    }

    /// <summary>A document's title as a PDF file's name: what a file's name cannot hold taken out, and ".pdf" put
    /// after all of it - a dot in the title is part of the title.</summary>
    internal static string PdfFileNameOf(string title)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
    }

    /// <summary>Writes <paramref name="sheets"/> as a PDF file at <paramref name="path"/> - beside it first, moved over
    /// it once whole, so a print called off or failed leaves no half of a PDF behind; false when the place would not
    /// take it.</summary>
    internal static async Task<bool> WritePdfAsync(PrintSheets sheets, string path, CancellationToken cancellation)
    {
        var writing = path + ".writing";
        try
        {
            await using (var output = File.Create(writing))
            {
                await PdfPrintFile.SaveAsync(sheets, sheets.Sheet, output, cancellation);
            }

            File.Move(writing, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(writing))
                {
                    File.Delete(writing);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left where it could not be taken away; the file asked for is untouched.
            }
        }
    }

    /// <summary>Progress nobody shows yet - a platform reports to it all the same.</summary>
    private sealed class Unwatched : IProgress<PrintProgress>
    {
        public static readonly Unwatched Instance = new();

        public void Report(PrintProgress value)
        {
        }
    }
}
