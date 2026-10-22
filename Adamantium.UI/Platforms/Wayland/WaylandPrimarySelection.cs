using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The primary selection (zwp_primary_selection_v1): selected text that a middle click pastes.</summary>
internal sealed unsafe class WaylandPrimarySelection : IPrimarySelection
{
    private static readonly string[] TextTypes = WaylandDataDevice.TypesOf(DataFormats.Text);

    private readonly WaylandPlatform platform;
    private readonly ZwpPrimarySelectionDeviceManagerV1 manager;
    private readonly ZwpPrimarySelectionDeviceV1 device;
    private readonly object sync = new();
    private readonly Dictionary<ZwpPrimarySelectionOfferV1, List<string>> announced = new();

    private ZwpPrimarySelectionOfferV1 selection;
    private List<string> selectionTypes;
    private ZwpPrimarySelectionSourceV1 source;
    private string sourceText;

    public WaylandPrimarySelection(WaylandPlatform platform, ZwpPrimarySelectionDeviceManagerV1 manager, WlSeat seat)
    {
        this.platform = platform;
        this.manager = manager;
        device = manager.GetDevice(seat);
        device.DataOffer += OnDataOffer;
        device.Selection += OnSelection;
    }

    public string GetText()
    {
        lock (sync)
        {
            if (source != null)
            {
                return sourceText;
            }

            if (selection == null)
            {
                return string.Empty;
            }
        }

        var fds = stackalloc int[2];
        if (PosixInterop.Pipe2(fds, PosixInterop.OpenCloseOnExec) != 0)
        {
            return string.Empty;
        }

        var readEnd = fds[0];
        var writeEnd = fds[1];
        var asked = new TaskCompletionSource<bool>();
        platform.Invoke(() =>
        {
            ZwpPrimarySelectionOfferV1 offer;
            string type;
            lock (sync)
            {
                offer = selection;
                type = selectionTypes == null ? null : Array.Find(TextTypes, selectionTypes.Contains);
            }

            var sent = offer is { IsAlive: true } && type != null;
            if (sent)
            {
                offer.Receive(type, writeEnd);
                platform.Display.Flush();
            }

            PosixInterop.Close(writeEnd);
            asked.TrySetResult(sent);
        });

        try
        {
            if (!asked.Task.Wait(TimeSpan.FromSeconds(1)) || !asked.Task.Result)
            {
                return string.Empty;
            }

            var bytes = WaylandDataDevice.ReadToEnd(readEnd);
            return bytes == null ? string.Empty : (string)WaylandDataDevice.Decode(DataFormats.Text, bytes);
        }
        finally
        {
            PosixInterop.Close(readEnd);
        }
    }

    public void SetText(string text)
    {
        lock (sync)
        {
            sourceText = text ?? string.Empty;
            if (source != null)
            {
                return;
            }

            source = manager.CreateSource();
            var offered = source;
            source.Send += (_, fd) => WaylandDataDevice.Send(() => Produce(offered), fd);
            source.Cancelled += () => Cancelled(offered);
            foreach (var type in TextTypes)
            {
                source.Offer(type);
            }

            device.SetSelection(source, platform.Seat.LastInputSerial);
        }

        platform.Display.Flush();
    }

    private byte[] Produce(ZwpPrimarySelectionSourceV1 offered)
    {
        lock (sync)
        {
            return source == offered ? WaylandDataDevice.EncodeFormat(DataFormats.Text, sourceText) : [];
        }
    }

    private void Cancelled(ZwpPrimarySelectionSourceV1 cancelled)
    {
        lock (sync)
        {
            if (source == cancelled)
            {
                source = null;
                sourceText = null;
            }
        }

        cancelled.Destroy();
    }

    private void OnDataOffer(ZwpPrimarySelectionOfferV1 offer)
    {
        var types = new List<string>();
        offer.Offer += type =>
        {
            lock (sync)
            {
                types.Add(type);
            }
        };
        lock (sync)
        {
            announced[offer] = types;
        }
    }

    private void OnSelection(ZwpPrimarySelectionOfferV1 offer)
    {
        ZwpPrimarySelectionOfferV1 previous;
        lock (sync)
        {
            previous = selection;
            selection = offer;
            selectionTypes = offer != null && announced.Remove(offer, out var types) ? types : null;
        }

        if (previous != null && previous != offer && previous.IsAlive)
        {
            previous.Destroy();
        }
    }
}
