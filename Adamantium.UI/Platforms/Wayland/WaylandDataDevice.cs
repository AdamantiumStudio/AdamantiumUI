using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Adamantium.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The seat's wl_data_device, shared by the clipboard and drag and drop: the offers of other clients, and the
/// pipes data travels through.</summary>
internal sealed unsafe class WaylandDataDevice
{
    private const int ReadTimeout = 1000;

    private static readonly string[] TextTypes = ["text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "STRING", "TEXT"];
    private static readonly string[] HtmlTypes = ["text/html"];
    private static readonly string[] RtfTypes = ["text/rtf", "application/rtf"];
    private static readonly string[] FileTypes = ["text/uri-list"];
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff"];

    private readonly WaylandPlatform platform;
    private readonly object sync = new();
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

    /// <summary>Another client's clipboard; null when it is empty or ours.</summary>
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

    /// <summary>A drag entered one of our surfaces, at a logical point of it.</summary>
    public event Action<uint, WlSurface, double, double, WaylandOffer> DragEntered;

    public event Action<double, double> DragMotion;

    public event Action DragLeft;

    /// <summary>The drag was dropped; its offer is taken with <see cref="TakeDragOffer"/>.</summary>
    public event Action DragDropped;

    /// <summary>Hands the dropped offer over: it is then the caller's to finish and destroy.</summary>
    public WaylandOffer TakeDragOffer()
    {
        var offer = dragOffer;
        dragOffer = null;
        return offer;
    }

    /// <summary>The MIME types a format travels as.</summary>
    public static string[] TypesOf(string format) => format switch
    {
        DataFormats.Text => TextTypes,
        DataFormats.Html => HtmlTypes,
        DataFormats.Rtf => RtfTypes,
        DataFormats.Files => FileTypes,
        DataFormats.Image => ImageTypes,
        _ => [format.Contains('/') ? format : "application/x-adamantium-" + format],
    };

    /// <summary>The bytes of every format of a package that crosses now; deferred ones are left out.</summary>
    public static Dictionary<string, byte[]> Encode(IDataPackage package)
    {
        var data = new Dictionary<string, byte[]>();
        foreach (var format in package.GetFormats())
        {
            if (!package.IsDeferred(format) && EncodeFormat(format, package.Get(format)) is { } bytes)
            {
                data[format] = bytes;
            }
        }

        return data;
    }

    /// <summary>What a drag offers other clients, each type made only when asked for; text last.</summary>
    public static List<(string Type, Func<byte[]> Produce)> Offers(IDataPackage package)
    {
        List<(string, Func<byte[]>)> offers = [];
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
            if (PictureType(picture) is { } type)
            {
                Add([type], () => picture);
            }
            else
            {
                Add(["image/bmp"], () => DragPicture.Convert(picture));
            }
        }

        foreach (var format in package.GetFormats())
        {
            if (format is DataFormats.Text or DataFormats.Files or DataFormats.Image)
            {
                continue;
            }

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

    public static byte[] EncodeFormat(string format, object value) => (format, value) switch
    {
        (DataFormats.Text or DataFormats.Html or DataFormats.Rtf, string text) => Encoding.UTF8.GetBytes(text),
        (DataFormats.Files, _) => FilesOf(value) is { Length: > 0 } files ? UriList(files) : null,
        (DataFormats.Image, _) => null,
        (_, byte[] own) => own,
        _ => null,
    };

    public static object Decode(string format, byte[] bytes) => format switch
    {
        DataFormats.Text or DataFormats.Html or DataFormats.Rtf => Encoding.UTF8.GetString(bytes),
        DataFormats.Files => Paths(Encoding.UTF8.GetString(bytes)),
        _ => bytes,
    };

    /// <summary>Writes the bytes to a client's pipe off the event thread.</summary>
    public static void Send(byte[] bytes, int fd) => Send(() => bytes, fd);

    /// <summary>Makes the bytes and writes them to a client's pipe off the event thread.</summary>
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
            }
        });
    }

    /// <summary>The bytes of the first of these types the offer has; null when it has none or does not answer in
    /// time.</summary>
    public byte[] Read(WaylandOffer offer, string[] types)
    {
        var type = offer?.FirstOf(types);
        if (type == null)
        {
            return null;
        }

        var fds = stackalloc int[2];
        if (PosixInterop.Pipe2(fds, PosixInterop.OpenCloseOnExec) != 0)
        {
            return null;
        }

        try
        {
            offer.Proxy.Receive(type, fds[1]);
        }
        catch (ObjectDisposedException)
        {
            PosixInterop.Close(fds[0]);
            PosixInterop.Close(fds[1]);
            return null;
        }

        PosixInterop.Close(fds[1]);
        platform.Display.Flush();

        try
        {
            return ReadToEnd(fds[0]);
        }
        finally
        {
            PosixInterop.Close(fds[0]);
        }
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

    private static string TextOf(IDataPackage package)
    {
        if (package.Get(DataFormats.Text) is string text)
        {
            return text;
        }

        if (package.Get<string>() is { } single)
        {
            return single;
        }

        if (package.Get<IEnumerable<string>>() is { } many)
        {
            return string.Join(Environment.NewLine, many);
        }

        return null;
    }

    private static string[] FilesOf(object value) => value switch
    {
        string[] paths => paths,
        IEnumerable<string> paths => [.. paths],
        string single => [single],
        _ => null,
    };

    private static byte[] UriList(string[] paths) =>
        Encoding.UTF8.GetBytes(string.Concat(paths.Select(path => new Uri(Path.GetFullPath(path)).AbsoluteUri + "\r\n")));

    private static string[] Paths(string list) => list
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Where(line => !line.StartsWith('#') && Uri.TryCreate(line.Trim(), UriKind.Absolute, out var uri) && uri.IsFile)
        .Select(line => new Uri(line.Trim()).LocalPath)
        .ToArray();

    /// <summary>What a client writes into the pipe until it closes it; null when it does not finish in time.</summary>
    internal static byte[] ReadToEnd(int fd)
    {
        using var result = new MemoryStream();
        var buffer = stackalloc byte[4096];
        var poll = new PosixPollFd { Fd = fd, Events = PosixInterop.PollIn };
        var deadline = Environment.TickCount64 + ReadTimeout;

        while (true)
        {
            var left = (int)(deadline - Environment.TickCount64);
            if (left <= 0 || PosixInterop.Poll(&poll, 1, left) <= 0)
            {
                return null;
            }

            var count = (int)PosixInterop.Read(fd, buffer, 4096);
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
}
