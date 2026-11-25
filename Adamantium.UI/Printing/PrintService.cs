using System;
using System.IO;
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
    public async Task<PrintResult> PrintAsync(IPrintDocument document, int currentPage = 0,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var platform = SystemPrinting.Platform;
        var parameters = new NavigationParameters()
            .Add(PrintDialogViewModel.DocumentKey, document)
            .Add(PrintDialogViewModel.PlatformKey, platform)
            .Add(PrintDialogViewModel.CurrentPageKey, currentPage);
        var answer = await dialogs.ShowDialogAsync(typeof(PrintDialogViewModel), parameters, DialogHostKind.Window,
            cancellation);
        if (answer?.Result != DialogButtonResult.Ok ||
            answer.Parameters?.GetValue<PrintSettings>(PrintDialogViewModel.SettingsKey) is not { } settings)
        {
            return PrintResult.Canceled;
        }

        var capabilities = settings.Printer.IsFile || platform == null
            ? PrinterCapabilities.Pdf
            : await platform.CapabilitiesAsync(settings.Printer, cancellation);
        var sheets = new PrintSheets(document, settings, capabilities, currentPage);
        if (!settings.Printer.IsFile)
        {
            await platform!.PrintAsync(sheets, settings, Unwatched.Instance, cancellation);
            return PrintResult.Printed;
        }

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
        return WritePdf(sheets, path, cancellation) ? PrintResult.Saved : PrintResult.Failed;
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
    internal static bool WritePdf(PrintSheets sheets, string path, CancellationToken cancellation)
    {
        var writing = path + ".writing";
        try
        {
            using (var output = File.Create(writing))
            {
                PdfPrintFile.Save(sheets, sheets.Sheet, output, cancellation);
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
