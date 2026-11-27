using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Printing;
using Adamantium.Win32.Printing;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>Windows' printers: the spooler's queues and their state, what each can do from its PrintCapabilities,
/// choices put right by the driver through its PrintTicket, and jobs sent as XPS through Print Document Package.
/// </summary>
internal sealed class WindowsPrintPlatform : IPrintPlatform
{
    private readonly Lock _gate = new();

    // What each printer was last described as - its schema and the ticket it starts from - by its queue's name.
    private readonly Dictionary<string, Described> _described = new();

    // The provider choices are put right through, kept open for the printer and the thread asking: the dialog asks on
    // every change, and opening one costs more than asking it.
    private PrintTicketProvider _reconciling;
    private string _reconcilingPrinter;
    private int _reconcilingThread;

    /// <summary>A printer described: its schema, the ticket it starts from, and the margins of each paper asked about
    /// so far - the same dictionary its capabilities carry.</summary>
    private sealed record Described(PrintSchema Schema, XDocument Ticket, Dictionary<string, Thickness> Margins);

    public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
        Task.Run(() => (IReadOnlyList<Printer>)Enumerate(), cancellation);

    public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (printer.IsFile)
        {
            return Task.FromResult(PrinterCapabilities.Pdf);
        }

        return Task.Run(() =>
        {
            var described = Describe(printer.Id, cancellation);
            lock (_gate)
            {
                // The margins of the papers asked about before stay known: the job is laid out on the paper chosen.
                if (_described.TryGetValue(printer.Id, out var before))
                {
                    foreach (var (paper, margins) in before.Margins)
                    {
                        described.Margins.TryAdd(paper, margins);
                    }
                }

                _described[printer.Id] = described;
            }

            return described.Schema.Capabilities;
        }, cancellation);
    }

    public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var described = DescribedOf(settings.Printer);
        if (described == null)
        {
            return settings;
        }

        PrintTicketProvider provider;
        XDocument valid;
        try
        {
            provider = ReconcilingProvider(settings.Printer.Id);
            valid = provider.Validate(described.Schema.Write(BaseTicket(described, settings), settings));
        }
        catch (ExternalException)
        {
            // The driver cannot be asked just now - a printer gone off the network: the choices stand as they are.
            return settings;
        }

        if (valid == null)
        {
            return settings;
        }

        var result = described.Schema.ReadBack(valid, settings);

        // A paper's margins are asked for when it is first chosen - asked for every paper at once, a printer with
        // dozens takes seconds - before the sheets are laid out on it.
        if (result.Paper != null && !described.Margins.ContainsKey(result.Paper.Name))
        {
            try
            {
                // Asked upright: margins are kept for the paper standing up, and turned with it when laid out.
                var upright = described.Schema.Write(valid, result with { Orientation = PageOrientation.Portrait });
                if (PrintSchema.MarginsIn(provider.Capabilities(upright), result.Paper) is { } margins)
                {
                    described.Margins[result.Paper.Name] = margins;
                }
            }
            catch (ExternalException)
            {
                // No margins said: the sheet is laid out to its edges, as for a printer that gives none.
            }
        }

        return result;
    }

    /// <summary>The driver's window over the window in front, starting from the ticket the settings make; its answer
    /// put right by the driver and read back - the dialog's options as it left them, the rest kept as the job's ticket.
    /// </summary>
    public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var described = DescribedOf(settings.Printer);
        if (described == null)
        {
            return null;
        }

        try
        {
            var provider = ReconcilingProvider(settings.Printer.Id);
            var ticket = described.Schema.Write(BaseTicket(described, settings), settings);
            var edited = DevMode.Edited(settings.Printer.Id, WindowsDialogOwner.Of(IntPtr.Zero),
                provider.ToDevMode(ticket));
            if (edited == null)
            {
                return null;
            }

            var answer = provider.FromDevMode(edited);
            answer = provider.Validate(answer) ?? answer;
            return described.Schema.ReadBack(answer, settings) with { DriverSettings = answer };
        }
        catch (ExternalException)
        {
            // The driver cannot be asked just now: nothing was changed.
            return null;
        }
    }

    /// <summary>The ticket a job starts from: what the driver's window was left with, else the printer's default.
    /// </summary>
    private static XDocument BaseTicket(Described described, PrintSettings settings) =>
        settings.DriverSettings as XDocument ?? described.Ticket;

    /// <summary>The provider of <paramref name="printer"/> on this thread, kept open while the same printer is asked
    /// about from it.</summary>
    private PrintTicketProvider ReconcilingProvider(string printer)
    {
        var thread = Environment.CurrentManagedThreadId;
        if (_reconciling != null && _reconcilingPrinter == printer && _reconcilingThread == thread)
        {
            return _reconciling;
        }

        _reconciling?.Dispose();
        _reconciling = PrintTicketProvider.Open(printer);
        _reconcilingPrinter = printer;
        _reconcilingThread = thread;
        return _reconciling;
    }

    public async Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(settings);
        var described = DescribedOf(settings.Printer) ?? Describe(settings.Printer.Id, cancellation);

        // On the thread printing was asked from: a document draws its pages there, as it does on screen. One not laid
        // out yet is laid out as the settings say, and made ready before the job starts.
        var laid = sheets as PrintSheets ?? new PrintSheets(sheets, settings, described.Schema.Capabilities);
        laid.Paginate(laid.Sheet);
        await laid.PrepareAsync(laid.Sheet, cancellation);

        var ticket = described.Schema.Write(BaseTicket(described, settings), settings);
        ticket = ReconcilingProvider(settings.Printer.Id).Validate(ticket) ?? ticket;
        await XpsPrintJob.PrintAsync(settings.Printer.Id, laid, laid.Sheet, ticket, null, progress, cancellation);
    }

    private Described DescribedOf(Printer printer)
    {
        if (printer == null || printer.IsFile)
        {
            return null;
        }

        lock (_gate)
        {
            return _described.GetValueOrDefault(printer.Id);
        }
    }

    /// <summary>The spooler's printers - local ones and connections - each with its state, the default marked.</summary>
    private static List<Printer> Enumerate()
    {
        var printers = new List<Printer>();
        var defaultName = DefaultPrinter();
        const uint Flags = Winspool.EnumLocal | Winspool.EnumConnections;
        Winspool.EnumPrinters(Flags, null, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return printers;
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!Winspool.EnumPrinters(Flags, null, 2, buffer, needed, out _, out var count))
            {
                return printers;
            }

            var size = Marshal.SizeOf<PrinterInfo2>();
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer + index * size);
                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                printers.Add(new Printer(name, name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                    StateOf(info), (int)info.Jobs));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return printers;
    }

    private static string DefaultPrinter()
    {
        uint size = 0;
        Winspool.GetDefaultPrinter(null, ref size);
        if (size == 0)
        {
            return null;
        }

        var name = new char[size];
        return Winspool.GetDefaultPrinter(name, ref size) ? new string(name, 0, (int)Math.Max(0, size - 1)) : null;
    }

    /// <summary>The state the dialog says: what holds the printer up first, then whether it is busy.</summary>
    internal static PrinterState StateOf(PrinterInfo2 info)
    {
        var status = info.Status;
        if ((info.Attributes & Winspool.AttributeWorkOffline) != 0 ||
            (status & (PrinterStatusFlags.Offline | PrinterStatusFlags.NotAvailable | PrinterStatusFlags.ServerOffline))
            != 0)
        {
            return PrinterState.Offline;
        }

        if ((status & PrinterStatusFlags.PaperOut) != 0)
        {
            return PrinterState.OutOfPaper;
        }

        if ((status & PrinterStatusFlags.PaperJam) != 0)
        {
            return PrinterState.PaperJam;
        }

        if ((status & PrinterStatusFlags.Paused) != 0)
        {
            return PrinterState.Paused;
        }

        if ((status & (PrinterStatusFlags.Error | PrinterStatusFlags.DoorOpen | PrinterStatusFlags.NoToner |
                       PrinterStatusFlags.UserIntervention | PrinterStatusFlags.PaperProblem)) != 0)
        {
            return PrinterState.Error;
        }

        return (status & (PrinterStatusFlags.Printing | PrinterStatusFlags.Busy | PrinterStatusFlags.Processing)) != 0 ||
               info.Jobs > 0
            ? PrinterState.Printing
            : PrinterState.Ready;
    }

    /// <summary>What the printer can do, from its PrintCapabilities, starting from its default ticket - and the margins
    /// of its default paper, which those capabilities give.</summary>
    private static Described Describe(string printer, CancellationToken cancellation)
    {
        using var provider = PrintTicketProvider.Open(printer);
        var ticket = provider.DefaultTicket();
        cancellation.ThrowIfCancellationRequested();
        var capabilities = provider.Capabilities(ticket);
        var margins = new Dictionary<string, Thickness>();
        var schema = PrintSchema.Read(capabilities, ticket, margins, hasDriverSettings: true);
        if (schema.Capabilities.DefaultPaper is { } paper && PrintSchema.MarginsIn(capabilities, paper) is { } thickness)
        {
            margins[paper.Name] = thickness;
        }

        return new Described(schema, ticket, margins);
    }
}

/// <summary>A printer's PrintTicket provider, used on the thread it was opened on and closed after.</summary>
internal sealed class PrintTicketProvider : IDisposable
{
    private readonly string _printer;
    private IntPtr _handle;

    private PrintTicketProvider(string printer, IntPtr handle)
    {
        _printer = printer;
        _handle = handle;
    }

    public static PrintTicketProvider Open(string printer)
    {
        Marshal.ThrowExceptionForHR(Winspool.PTOpenProvider(printer, 1, out var handle));
        return new PrintTicketProvider(printer, handle);
    }

    /// <summary>The ticket the printer prints with unless told otherwise - its driver's current defaults.</summary>
    public XDocument DefaultTicket()
    {
        var devMode = DevMode.Of(_printer);
        return devMode != null ? FromDevMode(devMode) : EmptyTicket();
    }

    /// <summary>A ticket from a DEVMODE - the driver's own window's answer, or its defaults.</summary>
    public XDocument FromDevMode(byte[] devMode)
    {
        using var output = new MemoryStream();
        var buffer = Marshal.AllocHGlobal(devMode.Length);
        try
        {
            Marshal.Copy(devMode, 0, buffer, devMode.Length);
            Marshal.ThrowExceptionForHR(Winspool.PTConvertDevModeToPrintTicket(_handle, (uint)devMode.Length, buffer,
                PrintTicketScope.Job, new ComStream(output)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return Load(output);
    }

    /// <summary><paramref name="ticket"/> as a DEVMODE - for the driver's own window to start from.</summary>
    public byte[] ToDevMode(XDocument ticket)
    {
        using var input = Save(ticket);
        Marshal.ThrowExceptionForHR(Winspool.PTConvertPrintTicketToDevMode(_handle, new ComStream(input),
            DefaultDevModeType.User, PrintTicketScope.Job, out var size, out var devMode, out _));
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(devMode, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            Winspool.PTReleaseMemory(devMode);
        }
    }

    public XDocument Capabilities(XDocument ticket)
    {
        using var input = ticket != null ? Save(ticket) : null;
        using var output = new MemoryStream();
        Marshal.ThrowExceptionForHR(Winspool.PTGetPrintCapabilities(_handle,
            input != null ? new ComStream(input) : null, new ComStream(output), out _));
        return Load(output);
    }

    /// <summary><paramref name="ticket"/> as the driver takes it, conflicts put right; null where it refuses it.
    /// </summary>
    public XDocument Validate(XDocument ticket)
    {
        using var input = Save(ticket);
        using var output = new MemoryStream();
        var result = Winspool.PTMergeAndValidatePrintTicket(_handle, new ComStream(input), null, PrintTicketScope.Job,
            new ComStream(output), out _);
        return result >= 0 && output.Length > 0 ? Load(output) : null;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            Winspool.PTCloseProvider(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static XDocument EmptyTicket() => new(new XElement(PrintSchema.Framework + "PrintTicket",
        new XAttribute(XNamespace.Xmlns + "psf", PrintSchema.Framework.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "psk", PrintSchema.Keywords.NamespaceName),
        new XAttribute("version", "1")));

    internal static MemoryStream Save(XDocument document)
    {
        var stream = new MemoryStream();
        document.Save(stream, SaveOptions.DisableFormatting);
        stream.Position = 0;
        return stream;
    }

    private static XDocument Load(MemoryStream stream)
    {
        stream.Position = 0;
        return XDocument.Load(stream);
    }
}

/// <summary>A printer's DEVMODE - its driver's settings as the old API keeps them.</summary>
internal static class DevMode
{
    /// <summary>The printer's current defaults; null where the spooler cannot say.</summary>
    public static byte[] Of(string printer) => Read(printer, IntPtr.Zero, null, prompt: false);

    /// <summary>The driver's own settings window over <paramref name="owner"/> - none: over no window - starting from
    /// <paramref name="current"/>: what it was left with, or null when it was canceled.</summary>
    public static byte[] Edited(string printer, IntPtr owner, byte[] current) =>
        Read(printer, owner, current, prompt: true);

    private static byte[] Read(string printer, IntPtr owner, byte[] current, bool prompt)
    {
        if (!Winspool.OpenPrinter(printer, out var handle, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var size = Winspool.DocumentProperties(owner, handle, printer, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0)
            {
                return null;
            }

            var output = Marshal.AllocHGlobal(Math.Max(size, current?.Length ?? 0));
            var input = IntPtr.Zero;
            try
            {
                var mode = Winspool.DevModeOutBuffer;
                if (current != null)
                {
                    input = Marshal.AllocHGlobal(current.Length);
                    Marshal.Copy(current, 0, input, current.Length);
                    mode |= Winspool.DevModeInBuffer;
                }

                if (prompt)
                {
                    mode |= Winspool.DevModeInPrompt;
                }

                var answer = Winspool.DocumentProperties(owner, handle, printer, output, input, mode);
                if (answer < 0 || (prompt && answer != Winspool.Ok))
                {
                    return null;
                }

                var bytes = new byte[size];
                Marshal.Copy(output, bytes, 0, size);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(output);
                if (input != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(input);
                }
            }
        }
        finally
        {
            Winspool.ClosePrinter(handle);
        }
    }
}
