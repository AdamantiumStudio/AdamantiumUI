using System.Collections.Generic;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The Wayland clipboard: text, HTML, RTF, files and one's own formats cross; pictures and live objects stay in
/// the process. What the application put there itself is read from memory.</summary>
internal sealed class WaylandClipboard : IClipboard
{
    private readonly WaylandPlatform platform;
    private readonly WaylandDataDevice device;
    private readonly object sync = new();

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
            device.Device.SetSelection(source, platform.Seat.LastInputSerial);
        }

        platform.Display.Flush();
    }

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
