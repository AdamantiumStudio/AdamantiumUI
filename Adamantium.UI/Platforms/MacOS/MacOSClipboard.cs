using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Imaging;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The macOS clipboard: text, HTML, RTF, pictures, files and one's own formats.</summary>
internal sealed class MacOSClipboard(IntPtr pasteboard) : IClipboard
{
    private const string Text = "public.utf8-plain-text";
    private const string Html = "public.html";
    private const string Rtf = "public.rtf";
    private const string FileUrl = "public.file-url";
    private const string OwnPrefix = "com.adamantium.format.";
    private const string Png = "public.png";
    private const string Jpeg = "public.jpeg";
    private const string Tiff = "public.tiff";
    private const string Gif = "com.compuserve.gif";
    private const string Bmp = "com.microsoft.bmp";
    private const string WebP = "org.webmproject.webp";

    private static readonly string[] Pictures = [Png, Jpeg, Gif, WebP, Tiff, Bmp];
    private readonly object _lock = new();

    public string GetText() => GetData(DataFormats.Text) as string ?? string.Empty;

    public bool HasText() => Contains(DataFormats.Text);

    public void SetText(string text)
    {
        var package = new DataPackage();
        package.Set(DataFormats.Text, text ?? string.Empty);
        SetData(package);
    }

    public void SetData(IDataPackage package)
    {
        if (package == null)
            return;

        InPool(() =>
        {
            var items = CreateItems(package, keepsPromises: false);
            if (items.Length > 0)
                NSPasteboard.Write(pasteboard, items);
            foreach (var item in items)
                NSObject.Release(item);
        });
    }

    /// <summary>Items of what the package holds, owned by the caller; none for a package of nothing a pasteboard takes. The
    /// first has it in every format, and its first file; each other file is one more. What the package promises is made
    /// now, or when it is asked for.</summary>
    internal static IntPtr[] CreateItems(IDataPackage package, bool keepsPromises)
    {
        var data = new List<(string Type, byte[] Bytes)>();
        var promised = new List<(string Type, string Format)>();
        var files = new List<string>();
        foreach (var format in package.GetFormats())
        {
            if (keepsPromises && package.IsDeferred(format))
                promised.Add((TypesOf(format)[0], format));
            else if (format == DataFormats.Files)
                files.AddRange((package.Get(format) as IEnumerable<string> ?? []).Where(path => !string.IsNullOrEmpty(path)));
            else if (Encode(format, package.Get(format)) is { } encoded)
                data.Add(encoded);
        }

        if (data.Count == 0 && promised.Count == 0 && files.Count == 0)
            return [];

        var items = new IntPtr[Math.Max(1, files.Count)];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = NSPasteboard.CreateItem();
            if (i < files.Count)
                NSPasteboard.SetData(items[i], FileBytes(files[i]), FileUrl);
        }

        foreach (var (type, bytes) in data)
            NSPasteboard.SetData(items[0], bytes, type);
        MacOSPasteboardPromise.Give(items[0], package, promised);
        return items;
    }

    private static byte[] FileBytes(string path) => Encoding.UTF8.GetBytes(NSURL.FromPath(path));

    /// <summary>A value of a format as a pasteboard has it of <paramref name="type"/>; null when there is none.</summary>
    internal static byte[] EncodeAs(string type, string format, object value) => type switch
    {
        FileUrl => (value as IEnumerable<string>)?.FirstOrDefault() is { Length: > 0 } path ? FileBytes(path) : null,
        Png when value is byte[] picture => DragPicture.IsPng(picture) ? picture : DragPicture.Convert(picture, ImageFileType.Png),
        _ => Encode(format, value)?.Bytes
    };

    /// <summary>The types of a drag that may be dropped.</summary>
    internal static string[] DroppedTypes { get; } = [FileUrl, Text, Html, Rtf, .. Pictures];

    /// <summary>What is on the pasteboard, read now, in the formats applications have in common.</summary>
    internal IDataPackage Read()
    {
        var package = new DataPackage();
        foreach (var format in (string[])[DataFormats.Files, DataFormats.Text, DataFormats.Html, DataFormats.Rtf, DataFormats.Image])
        {
            if (GetData(format) is { } value)
                package.Set(format, value);
        }

        return package;
    }

    public bool Contains(string format)
    {
        var contains = false;
        InPool(() => contains = TypesOf(format).Any(type => NSPasteboard.Contains(pasteboard, type)));
        return contains;
    }

    public object GetData(string format)
    {
        object value = null;
        InPool(() => value = format == DataFormats.Files ? Files() : Decode(format, Read(format)));
        return value;
    }

    private byte[] Read(string format) =>
        TypesOf(format).Select(type => NSPasteboard.DataOf(pasteboard, type)).FirstOrDefault(bytes => bytes != null);

    private string[] Files()
    {
        var items = NSPasteboard.ItemsOf(pasteboard);
        var paths = new List<string>();
        for (var i = 0; i < NSArray.CountOf(items); i++)
        {
            if (NSPasteboard.DataOf(NSArray.ObjectAt(items, i), FileUrl) is { } url &&
                NSURL.ToPath(Encoding.UTF8.GetString(url)) is { } path)
            {
                paths.Add(path);
            }
        }

        return paths.Count > 0 ? paths.ToArray() : null;
    }

    private void InPool(Action action)
    {
        lock (_lock)
        {
            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                action();
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        }
    }

    private static (string Type, byte[] Bytes)? Encode(string format, object value) => (format, value) switch
    {
        (DataFormats.Text, string text) => (Text, Encoding.UTF8.GetBytes(text)),
        (DataFormats.Html, string html) => (Html, Encoding.UTF8.GetBytes(html)),
        (DataFormats.Rtf, string rtf) => (Rtf, Encoding.UTF8.GetBytes(rtf)),
        (DataFormats.Image, byte[] picture) => PictureType(picture) is { } type ? (type, picture) : null,
        (DataFormats.Text or DataFormats.Html or DataFormats.Rtf or DataFormats.Image, _) => null,
        (_, byte[] own) => (OwnType(format), own),
        _ => null
    };

    private static object Decode(string format, byte[] bytes) => bytes == null ? null : format switch
    {
        DataFormats.Text or DataFormats.Html or DataFormats.Rtf => Encoding.UTF8.GetString(bytes),
        _ => bytes
    };

    private static string[] TypesOf(string format) => format switch
    {
        DataFormats.Text => [Text],
        DataFormats.Html => [Html],
        DataFormats.Rtf => [Rtf],
        DataFormats.Files => [FileUrl],
        DataFormats.Image => Pictures,
        _ => [OwnType(format)]
    };

    // A type is named in letters, digits, dots and hyphens only; what is not one of them is written by its code.
    private static string OwnType(string format)
    {
        var type = new StringBuilder(OwnPrefix);
        foreach (var character in format)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '.')
                type.Append(character);
            else
                type.Append('-').Append(((int)character).ToString("x4"));
        }

        return type.ToString();
    }

    private static string PictureType(ReadOnlySpan<byte> picture)
    {
        if (picture.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]))
            return Png;
        if (picture.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return Jpeg;
        if (picture.StartsWith("GIF8"u8))
            return Gif;
        if (picture.StartsWith("II*\0"u8) || picture.StartsWith("MM\0*"u8))
            return Tiff;
        if (picture.StartsWith("BM"u8))
            return Bmp;

        return picture.Length >= 12 && picture.StartsWith("RIFF"u8) && picture[8..].StartsWith("WEBP"u8) ? WebP : null;
    }
}
