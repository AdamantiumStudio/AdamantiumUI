using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// The Wayland clipboard, through wl_data_device. What crosses is what crosses on Windows: text, HTML and RTF as
/// strings, files as paths, and formats of one's own as bytes under their name; pictures and live objects stay in the
/// process.
/// <para>
/// Nothing is copied when the clipboard is set: the client offers MIME types, and the bytes go through a pipe when
/// another client asks for one. Reading works the other way round, with a pipe the other client writes into. What this
/// application put on the clipboard itself is read from memory - a pipe it would have to answer on the thread that
/// waits for it.
/// </para>
/// </summary>
internal sealed class WaylandClipboard : IClipboard
{
    private readonly WaylandPlatform platform;
    private readonly WaylandDataDevice device;
    private readonly object sync = new();

    // What this application offers, by format, while it holds the clipboard.
    private WlDataSource source;
    private Dictionary<string, byte[]> sourceData;

    public WaylandClipboard(WaylandPlatform platform, WaylandDataDevice device)
    {
        this.platform = platform;
        this.device = device;
    }

    public string GetText() => GetData(DataFormats.Text) as string ?? string.Empty;

    public bool HasText() => Contains(DataFormats.Text);

    public void SetText(string text) =>
        Offer(new Dictionary<string, byte[]> { [DataFormats.Text] = WaylandDataDevice.EncodeFormat(DataFormats.Text, text ?? string.Empty) });

    public void SetData(IDataPackage package)
    {
        if (package == null) return;

        // A package with nothing that crosses leaves what is there, as on Windows.
        var data = WaylandDataDevice.Encode(package);
        if (data.Count > 0)
        {
            Offer(data);
        }
    }

    public bool Contains(string format)
    {
        lock (sync)
        {
            if (sourceData != null)
            {
                return sourceData.ContainsKey(format);
            }
        }

        return device.Selection?.FirstOf(WaylandDataDevice.TypesOf(format)) != null;
    }

    public object GetData(string format)
    {
        byte[] bytes;
        lock (sync)
        {
            bytes = sourceData?.GetValueOrDefault(format);
            if (sourceData != null && bytes == null)
            {
                return null;
            }
        }

        bytes ??= device.Read(device.Selection, WaylandDataDevice.TypesOf(format));
        return bytes == null ? null : WaylandDataDevice.Decode(format, bytes);
    }

    private void Offer(Dictionary<string, byte[]> data)
    {
        var byType = new Dictionary<string, byte[]>();
        foreach (var (format, bytes) in data)
        {
            foreach (var type in WaylandDataDevice.TypesOf(format))
            {
                byType[type] = bytes;
            }
        }

        lock (sync)
        {
            source?.Destroy();
            source = device.Manager.CreateDataSource();
            var offered = source;
            source.Send += (type, fd) => WaylandDataDevice.Send(byType.GetValueOrDefault(type), fd);
            source.Cancelled += () => Cancelled(offered);
            foreach (var type in byType.Keys)
            {
                source.Offer(type);
            }

            sourceData = data;

            // Asked for with the input that asked for the copy: the compositor takes the clipboard only from a client
            // the user is acting in.
            device.Device.SetSelection(source, platform.Seat.LastInputSerial);
        }

        platform.Display.Flush();
    }

    // Another client took the clipboard.
    private void Cancelled(WlDataSource cancelled)
    {
        lock (sync)
        {
            if (source == cancelled)
            {
                source = null;
                sourceData = null;
            }
        }

        cancelled.Destroy();
    }
}
