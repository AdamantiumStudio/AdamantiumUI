using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.Core.Input;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The Win32 clipboard - text (CF_UNICODETEXT), HTML, RTF, pictures and registered formats of one's own -
/// registered on <see cref="Clipboard.Current"/> by the Windows platform. All calls run on the UI (message-pump)
/// thread, as clipboard ownership is thread-affine. Every operation is best-effort: if the OS clipboard stays locked by
/// another app, it fails quietly rather than throwing into the input handler.</summary>
internal sealed class WindowsClipboard : IClipboard
{
    // Another application - the clipboard history among them - opens the clipboard as soon as it changes; a few short
    // tries get past that.
    private const int OpenTries = 10;

    private static bool Open()
    {
        for (var attempt = 0; attempt < OpenTries; attempt++)
        {
            if (Win32Interop.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            System.Threading.Thread.Sleep(5);
        }

        return false;
    }

    public string GetText()
    {
        if (!Win32Interop.IsClipboardFormatAvailable(Win32Interop.CF_UNICODETEXT)) return string.Empty;
        if (!Open()) return string.Empty;
        try
        {
            var handle = Win32Interop.GetClipboardData(Win32Interop.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return string.Empty;
            var ptr = Win32Interop.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return string.Empty;
            try { return Marshal.PtrToStringUni(ptr) ?? string.Empty; }
            finally { Win32Interop.GlobalUnlock(handle); }
        }
        finally { Win32Interop.CloseClipboard(); }
    }

    public bool HasText() => Win32Interop.IsClipboardFormatAvailable(Win32Interop.CF_UNICODETEXT);

    public void SetText(string text)
    {
        text ??= string.Empty;
        if (!Open()) return;
        try
        {
            Win32Interop.EmptyClipboard();

            var bytes = (UIntPtr)((text.Length + 1) * sizeof(char));   // + terminating NUL
            var hGlobal = Win32Interop.GlobalAlloc(Win32Interop.GMEM_MOVEABLE, bytes);
            if (hGlobal == IntPtr.Zero) return;

            var target = Win32Interop.GlobalLock(hGlobal);
            if (target == IntPtr.Zero) return;
            try
            {
                var chars = (text + '\0').ToCharArray();
                Marshal.Copy(chars, 0, target, chars.Length);
            }
            finally { Win32Interop.GlobalUnlock(hGlobal); }

            // On success the clipboard owns hGlobal - do not free it here.
            Win32Interop.SetClipboardData(Win32Interop.CF_UNICODETEXT, hGlobal);
        }
        finally { Win32Interop.CloseClipboard(); }
    }

    public void SetData(IDataPackage package)
    {
        if (package == null) return;

        // Rendered before the clipboard is opened and emptied: a package with nothing that crosses leaves what is there.
        var blocks = new List<(uint Id, IntPtr Global)>();
        foreach (var format in package.GetFormats())
        {
            if (format == DataFormats.Image && package.Get(format) is byte[] { Length: > 0 } picture)
            {
                if (OleDataBridge.PictureFormat(picture) is var named and not 0 &&
                    OleDataBridge.CreateBytesGlobal(picture) is var encoded && encoded != IntPtr.Zero)
                {
                    blocks.Add((named, encoded));
                }

                if (Dib(picture) is var dib && dib != IntPtr.Zero)
                {
                    blocks.Add((Win32Interop.CF_DIB, dib));
                }

                continue;
            }

            var block = package.Get(format) switch
            {
                string text when format == DataFormats.Text =>
                    (Win32Interop.CF_UNICODETEXT, OleDataBridge.CreateTextGlobal(text)),
                string html when format == DataFormats.Html =>
                    (OleDataBridge.RegisteredFormat(format), OleDataBridge.CreateHtmlGlobal(html)),
                string rtf when format == DataFormats.Rtf =>
                    (OleDataBridge.RegisteredFormat(format), OleDataBridge.CreateAnsiGlobal(rtf)),
                byte[] bytes when format is not (DataFormats.Text or DataFormats.Html or DataFormats.Rtf
                    or DataFormats.Image or DataFormats.Files) =>
                    (OleDataBridge.RegisteredFormat(format), OleDataBridge.CreateBytesGlobal(bytes)),
                _ => (0u, IntPtr.Zero),
            };
            if (block.Item1 != 0 && block.Item2 != IntPtr.Zero)
            {
                blocks.Add(block);
            }
        }

        if (blocks.Count == 0) return;
        if (!Open())
        {
            foreach (var (_, global) in blocks) Win32Interop.GlobalFree(global);
            return;
        }

        try
        {
            Win32Interop.EmptyClipboard();
            foreach (var (id, global) in blocks)
            {
                // On success the clipboard owns the block; on failure it is still ours.
                if (Win32Interop.SetClipboardData(id, global) == IntPtr.Zero)
                {
                    Win32Interop.GlobalFree(global);
                }
            }
        }
        finally { Win32Interop.CloseClipboard(); }
    }

    public bool Contains(string format) => format == DataFormats.Image
        ? Win32Interop.IsClipboardFormatAvailable(Win32Interop.CF_DIB) ||
          Win32Interop.IsClipboardFormatAvailable(OleDataBridge.TiffFormat) ||
          OleDataBridge.PictureFormats.Any(id => Win32Interop.IsClipboardFormatAvailable(id))
        : Id(format) is var id and not 0 && Win32Interop.IsClipboardFormatAvailable(id);

    public object GetData(string format)
    {
        if (format == DataFormats.Text) return Contains(format) ? GetText() : null;
        if (format == DataFormats.Image)
        {
            return Contains(format) ? GetPicture() : null;
        }

        if (!Contains(format) || !Open()) return null;
        try
        {
            var handle = Win32Interop.GetClipboardData(Id(format));
            if (handle == IntPtr.Zero || OleDataBridge.BytesOf(handle) is not { } bytes) return null;
            return format switch
            {
                DataFormats.Html => OleDataBridge.HtmlFragment(bytes),
                DataFormats.Rtf => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                _ => bytes,
            };
        }
        finally { Win32Interop.CloseClipboard(); }
    }

    private static IntPtr Dib(byte[] picture)
    {
        try
        {
            return OleDataBridge.CreateDibGlobal(picture);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static byte[] GetPicture()
    {
        if (!Open())
        {
            return null;
        }

        try
        {
            foreach (var id in OleDataBridge.PictureFormats)
            {
                if (Win32Interop.GetClipboardData(id) is var handle && handle != IntPtr.Zero &&
                    OleDataBridge.BytesOf(handle) is { Length: > 0 } encoded)
                {
                    return OleDataBridge.Unpadded(encoded);
                }
            }

            var dib = Win32Interop.GetClipboardData(Win32Interop.CF_DIB);
            if (dib != IntPtr.Zero && OleDataBridge.BytesOf(dib) is { Length: > 40 } bytes)
            {
                return OleDataBridge.BmpFileFromDib(bytes);
            }

            var tiff = Win32Interop.GetClipboardData(OleDataBridge.TiffFormat);
            return tiff != IntPtr.Zero && OleDataBridge.BytesOf(tiff) is { Length: > 0 } read ? read : null;
        }
        finally { Win32Interop.CloseClipboard(); }
    }

    private static uint Id(string format) =>
        format == DataFormats.Text ? Win32Interop.CF_UNICODETEXT : OleDataBridge.RegisteredFormat(format);
}
