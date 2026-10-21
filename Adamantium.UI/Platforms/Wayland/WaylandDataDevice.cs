using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// The seat's wl_data_device, which the clipboard and drag and drop share: the offers other clients make, with the MIME
/// types they announce, and the pipes the bytes go through either way.
/// <para>
/// An offer comes first as data_offer with its types, and only then says what it is for: the selection, or the drag
/// that has just entered one of our surfaces. The selection is the clipboard's to keep; the drag's offer lives until the
/// drag leaves, or until whoever takes its drop lets go of it.
/// </para>
/// </summary>
internal sealed unsafe class WaylandDataDevice
{
    // How long a read waits for the other client to write before giving up: a hung application must not hang this one.
    private const int ReadTimeout = 1000;

    // Text under every name it travels by: XWayland's X11 targets among them.
    private static readonly string[] TextTypes = ["text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "STRING", "TEXT"];
    private static readonly string[] HtmlTypes = ["text/html"];
    private static readonly string[] RtfTypes = ["text/rtf", "application/rtf"];
    private static readonly string[] FileTypes = ["text/uri-list"];
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff"];

    private readonly WaylandPlatform platform;
    private readonly object sync = new();

    // Offers announced and not yet named the selection or a drag.
    private readonly Dictionary<WlDataOffer, WaylandOffer> announced = new();
    private WaylandOffer selection;
    private WaylandOffer dragOffer;

    public WaylandDataDevice(WaylandPlatform platform, WlDataDeviceManager manager, WlSeat seat)
    {
        this.platform = platform;
        Manager = manager;
        Device = manager.GetDataDevice(seat);
        Device.DataOffer += OnDataOffer;
        Device.Selection += OnSelection;
        Device.Enter += OnEnter;
        Device.Leave += OnLeave;
        Device.Motion += (time, x, y) => DragMotion?.Invoke(x, y);
        Device.Drop += () => DragDropped?.Invoke();
    }

    public WlDataDeviceManager Manager { get; }

    public WlDataDevice Device { get; }

    /// <summary>What another client has on the clipboard; null when it is empty, or this application's.</summary>
    public WaylandOffer Selection
    {
        get
        {
            lock (sync)
            {
                return selection;
            }
        }
    }

    /// <summary>A drag came over one of our surfaces, at a point of it in logical pixels, carrying an offer.</summary>
    public event Action<uint, WlSurface, double, double, WaylandOffer> DragEntered;

    /// <summary>The drag moved over the surface it entered.</summary>
    public event Action<double, double> DragMotion;

    /// <summary>The drag left the surface, or was let go of where nobody took it.</summary>
    public event Action DragLeft;

    /// <summary>The drag was dropped on the surface, and its offer is the drop's: see <see cref="TakeDragOffer"/>.</summary>
    public event Action DragDropped;

    /// <summary>Hands the drag's offer to whoever takes the drop: the leave that follows does not destroy it then, and
    /// it is theirs to finish and destroy once the bytes are read.</summary>
    public WaylandOffer TakeDragOffer()
    {
        var offer = dragOffer;
        dragOffer = null;
        return offer;
    }

    #region MIME types

    /// <summary>The MIME types a format travels as: one's own formats under their name, made a MIME type when it is
    /// not one already.</summary>
    public static string[] TypesOf(string format) => format switch
    {
        DataFormats.Text => TextTypes,
        DataFormats.Html => HtmlTypes,
        DataFormats.Rtf => RtfTypes,
        DataFormats.Files => FileTypes,
        DataFormats.Image => ImageTypes,
        _ => [format.Contains('/') ? format : "application/x-adamantium-" + format],
    };

    /// <summary>The bytes what a package holds crosses as, by format: text, HTML and RTF as UTF-8, files as a URI list,
    /// and formats of one's own as their bytes. Pictures and live objects stay in the process.</summary>
    public static Dictionary<string, byte[]> Encode(IDataPackage package)
    {
        var data = new Dictionary<string, byte[]>();
        foreach (var format in package.GetFormats())
        {
            // A deferred format is made when another client asks for it, not before: a file written for a picture.
            if (package.IsDeferred(format))
            {
                continue;
            }

            if (EncodeFormat(format, package.Get(format)) is { } bytes)
            {
                data[format] = bytes;
            }
        }

        return data;
    }

    /// <summary>What a drag offers other clients, each type with how its bytes are made when a client asks: a promised
    /// file is written, a picture converted, only for the drop that wants it. Text comes last - a target takes the first
    /// type it understands, and a picture that carries its caption must not arrive as the caption. Pictures go out
    /// under their own encoding, as on Windows; one that few applications read goes out as a BMP.</summary>
    public static List<(string Type, Func<byte[]> Produce)> Offers(IDataPackage package)
    {
        var offers = new List<(string, Func<byte[]>)>();
        void Add(string[] types, Func<byte[]> produce)
        {
            var once = new Lazy<byte[]>(produce);
            offers.AddRange(types.Select(type => (type, (Func<byte[]>)(() => once.Value))));
        }

        if (package.Contains(DataFormats.Files) || FilesOf(package.Get<string[]>()) != null)
        {
            Add(FileTypes, () => FilesOf(package.Get(DataFormats.Files) ?? package.Get<string[]>()) is { Length: > 0 } files
                ? UriList(files)
                : null);
        }

        if (package.Get(DataFormats.Image) is byte[] { Length: > 0 } picture)
        {
            if (PictureType(picture) is { } type) Add([type], () => picture);
            else Add(["image/bmp"], () => DragPicture.Convert(picture));
        }

        foreach (var format in package.GetFormats())
        {
            if (format is DataFormats.Text or DataFormats.Files or DataFormats.Image)
            {
                continue;
            }

            // A live object stays in the process: what crosses is markup, bytes, or a promise of them.
            if (format is DataFormats.Html or DataFormats.Rtf || package.IsDeferred(format) || package.Get(format) is byte[])
            {
                Add(TypesOf(format), () => EncodeFormat(format, package.Get(format)));
            }
        }

        if (TextOf(package) != null)
        {
            Add(TextTypes, () => Encoding.UTF8.GetBytes(TextOf(package)));
        }

        return offers;
    }

    private static string PictureType(byte[] picture) => DragPicture.Extension(picture) switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" => "image/tiff",
        _ => null,
    };

    // A package's text: its text format, else a bare string payload, else a list of them a line each - what an editor
    // expects from a dragged multi-selection.
    private static string TextOf(IDataPackage package)
    {
        if (package.Get(DataFormats.Text) is string text) return text;
        if (package.Get<string>() is { } single) return single;
        if (package.Get<IEnumerable<string>>() is { } many) return string.Join(Environment.NewLine, many);
        return null;
    }

    public static byte[] EncodeFormat(string format, object value) => (format, value) switch
    {
        (DataFormats.Text or DataFormats.Html or DataFormats.Rtf, string text) => Encoding.UTF8.GetBytes(text),
        (DataFormats.Files, _) => FilesOf(value) is { Length: > 0 } files ? UriList(files) : null,
        (DataFormats.Image, _) => null,
        (_, byte[] own) => own,
        _ => null,
    };

    /// <summary>What the bytes of a format read back as: strings for the text formats, paths for files.</summary>
    public static object Decode(string format, byte[] bytes) => format switch
    {
        DataFormats.Text or DataFormats.Html or DataFormats.Rtf => Encoding.UTF8.GetString(bytes),
        DataFormats.Files => Paths(Encoding.UTF8.GetString(bytes)),
        _ => bytes,
    };

    private static string[] FilesOf(object value) => value switch
    {
        string[] paths => paths,
        IEnumerable<string> paths => [.. paths],
        string single => [single],
        _ => null,
    };

    private static byte[] UriList(string[] paths) =>
        Encoding.UTF8.GetBytes(string.Concat(paths.Select(path => new Uri(Path.GetFullPath(path)).AbsoluteUri + "\r\n")));

    // A URI list keeps comments after #, and only file URIs are paths here.
    private static string[] Paths(string list) => list
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Where(line => !line.StartsWith('#') && Uri.TryCreate(line.Trim(), UriKind.Absolute, out var uri) && uri.IsFile)
        .Select(line => new Uri(line.Trim()).LocalPath)
        .ToArray();

    #endregion

    #region Offers

    private void OnDataOffer(WlDataOffer proxy)
    {
        var offer = new WaylandOffer(proxy);
        lock (sync)
        {
            announced[proxy] = offer;
        }
    }

    private WaylandOffer Claim(WlDataOffer proxy)
    {
        if (proxy == null)
        {
            return null;
        }

        lock (sync)
        {
            return announced.Remove(proxy, out var offer) ? offer : null;
        }
    }

    private void OnSelection(WlDataOffer proxy)
    {
        var offer = Claim(proxy);
        WaylandOffer previous;
        lock (sync)
        {
            previous = selection;
            selection = offer;
        }

        if (previous != null && previous != offer)
        {
            previous.Destroy();
        }
    }

    private void OnEnter(uint serial, WlSurface surface, double x, double y, WlDataOffer proxy)
    {
        dragOffer?.Destroy();
        dragOffer = Claim(proxy);
        DragEntered?.Invoke(serial, surface, x, y, dragOffer);
    }

    private void OnLeave()
    {
        DragLeft?.Invoke();
        dragOffer?.Destroy();
        dragOffer = null;
    }

    #endregion

    #region Pipes

    /// <summary>A source's answer to a send: the bytes, written off the event thread - the reader may be slow, and
    /// this thread is the one every event waits for. Unknown types get nothing.</summary>
    public static void Send(byte[] bytes, int fd) => Send(() => bytes, fd);

    /// <summary>The same, with the bytes made off the event thread too: a promised file written, a picture converted.</summary>
    public static void Send(Func<byte[]> produce, int fd)
    {
        Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, true), FileAccess.Write);
                stream.Write(produce?.Invoke() ?? []);
            }
            catch (IOException)
            {
                // The reader went away: nothing to tell anyone.
            }
        });
    }

    /// <summary>The bytes of the first of these types the offer has, through a pipe the offering client writes to;
    /// null when it has none, or does not answer in time.</summary>
    public byte[] Read(WaylandOffer offer, string[] types)
    {
        var type = offer?.FirstOf(types);
        if (type == null)
        {
            return null;
        }

        var fds = stackalloc int[2];
        if (LinuxInterop.pipe2(fds, LinuxInterop.OpenCloseOnExec) != 0)
        {
            return null;
        }

        try
        {
            offer.Proxy.Receive(type, fds[1]);
        }
        catch (ObjectDisposedException)
        {
            // The offer went under the read.
            LinuxInterop.close(fds[0]);
            LinuxInterop.close(fds[1]);
            return null;
        }

        // The write end is the other client's now; ours goes, or the read would never see the end.
        LinuxInterop.close(fds[1]);
        platform.Display.Flush();

        try
        {
            return ReadToEnd(fds[0]);
        }
        finally
        {
            LinuxInterop.close(fds[0]);
        }
    }

    private static byte[] ReadToEnd(int fd)
    {
        using var result = new MemoryStream();
        var buffer = stackalloc byte[4096];
        var poll = new LinuxInterop.PollFd { Fd = fd, Events = LinuxInterop.PollIn };
        var deadline = Environment.TickCount64 + ReadTimeout;

        while (true)
        {
            var left = (int)(deadline - Environment.TickCount64);
            if (left <= 0 || LinuxInterop.poll(&poll, 1, left) <= 0)
            {
                return null;
            }

            var count = (int)LinuxInterop.read(fd, buffer, 4096);
            if (count == 0)
            {
                return result.ToArray();
            }

            if (count < 0)
            {
                return null;
            }

            result.Write(new ReadOnlySpan<byte>(buffer, count));
        }
    }

    #endregion
}

/// <summary>An offer of another client's - or of this application's own drag - with what it says about itself.</summary>
internal sealed class WaylandOffer
{
    private readonly List<string> types = new();

    public WaylandOffer(WlDataOffer proxy)
    {
        Proxy = proxy;
        proxy.Offer += type =>
        {
            lock (types)
            {
                types.Add(type);
            }
        };
        proxy.SourceActions += actions => SourceActions = actions;
        proxy.Action += action => Action = action;
    }

    public WlDataOffer Proxy { get; }

    /// <summary>What the dragging client allows.</summary>
    public WlDataDeviceManager.DndAction SourceActions { get; private set; }

    /// <summary>What the compositor settled on between the source, the target and the keys held.</summary>
    public WlDataDeviceManager.DndAction Action { get; private set; }

    public bool Has(string type)
    {
        lock (types)
        {
            return types.Contains(type);
        }
    }

    public string FirstOf(string[] wanted)
    {
        lock (types)
        {
            return wanted.FirstOrDefault(types.Contains);
        }
    }

    public string First()
    {
        lock (types)
        {
            return types.FirstOrDefault();
        }
    }

    public void Destroy()
    {
        if (Proxy.IsAlive)
        {
            Proxy.Destroy();
        }
    }
}
