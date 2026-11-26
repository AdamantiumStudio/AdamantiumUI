using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Adamantium.UI.Core.Printing;
using Adamantium.Win32;
using Adamantium.Win32.Printing;
using Adamantium.Win32.Printing.Xps;
using static Adamantium.UI.Platforms.Windows.Printing.XpsGeometryPath;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>A print job on Windows: the sheets written as XPS, page by page, into a Print Document Package job with
/// the job's PrintTicket - or into a stream, for a package of its own.</summary>
internal static class XpsPrintJob
{
    private static readonly Guid XpsTargetInterface = typeof(IXpsDocumentPackageTarget).GUID;

    /// <summary>Prints <paramref name="sheets"/> on <paramref name="printer"/>; <paramref name="output"/>, when given,
    /// takes what the driver makes instead of the device. Called off by <paramref name="cancellation"/>, the job is
    /// canceled and nothing is printed.</summary>
    public static void Print(string printer, IPrintDocument sheets, PrintPage sheet, XDocument ticket, Stream output,
        IProgress<PrintProgress> progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var factoryClass = PrintDocumentPackage.TargetFactoryClass;
        var factoryInterface = typeof(IPrintDocumentPackageTargetFactory).GUID;
        Marshal.ThrowExceptionForHR(Win32Interop.CoCreateInstance(ref factoryClass, IntPtr.Zero,
            Win32Interop.ClsCtxInprocServer, ref factoryInterface, out var created));
        var factory = (IPrintDocumentPackageTargetFactory)created;
        IPrintDocumentPackageTarget target = null;
        try
        {
            using var ticketStream = ticket != null ? PrintTicketProvider.Save(ticket) : null;
            Check(factory.CreateDocumentPackageTargetForPrintJob(printer, sheets.Title ?? "Document",
                output != null ? new ComStream(output) : null,
                ticketStream != null ? new ComStream(ticketStream) : null, out target));
            Check(target.GetPackageTarget(PrintDocumentPackage.MsXpsTarget, XpsTargetInterface, out var xps));
            var package = (IXpsDocumentPackageTarget)xps;
            try
            {
                Check(package.GetXpsOMFactory(out var objects));
                try
                {
                    Check(objects.CreatePartUri("/FixedDocumentSequence.fdseq", out var sequence));
                    Check(package.GetXpsOMPackageWriter(sequence, null, out var writer));
                    try
                    {
                        Write(objects, writer, sheets, sheet, progress, cancellation);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(writer);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(objects);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(package);
            }
        }
        catch
        {
            target?.Cancel();
            throw;
        }
        finally
        {
            if (target != null)
            {
                Marshal.ReleaseComObject(target);
            }

            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Writes the sheets as one fixed document through <paramref name="writer"/>, then closes the package.
    /// </summary>
    internal static void Write(IXpsOMObjectFactory objects, IXpsOMPackageWriter writer, IPrintDocument sheets,
        PrintPage sheet, IProgress<PrintProgress> progress, CancellationToken cancellation)
    {
        Check(objects.CreatePartUri("/Documents/1/FixedDocument.fdoc", out var document));
        Check(writer.StartNewDocument(document, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        var count = Math.Max(1, sheets.Paginate(sheet));
        var size = new XpsSize((float)sheet.Size.Width, (float)sheet.Size.Height);
        var language = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US";
        using var pictures = new XpsPictures(objects);
        using var fonts = new XpsFonts(objects);
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var part = "/Documents/1/Pages/" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".fpage";
            Check(objects.CreatePartUri(part, out var pageName));
            Check(objects.CreatePage(size, language, pageName, out var page));
            try
            {
                Check(page.GetVisuals(out var visuals));
                try
                {
                    var session = new XpsDrawingSession(objects, pictures, fonts, visuals);
                    sheets.DrawPage(index, session);
                    session.CloseGroups();
                }
                finally
                {
                    Marshal.ReleaseComObject(visuals);
                }

                // The page's fonts are written with the glyphs it used, then the page with them.
                fonts.WritePage();
                Check(writer.AddPage(page, size, IntPtr.Zero, IntPtr.Zero, null, IntPtr.Zero));
            }
            finally
            {
                fonts.EndPage();
                Marshal.ReleaseComObject(page);
            }

            progress?.Report(new PrintProgress(index + 1, count));
        }

        Check(writer.Close());
    }
}
