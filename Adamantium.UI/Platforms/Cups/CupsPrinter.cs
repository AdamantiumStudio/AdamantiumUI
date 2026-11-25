using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Platforms.Cups;

/// <summary>A printer of the system's print server, opened to be asked and printed on - by one thread at a time, as
/// libcups is.</summary>
internal sealed unsafe class CupsPrinter : IDisposable
{
    private const int Chunk = 65536;

    private readonly CupsDest* _dest;
    private readonly nint _info;

    private CupsPrinter(CupsDest* dest, nint info)
    {
        _dest = dest;
        _info = info;
    }

    /// <summary>The printers the server knows, the default marked.</summary>
    public static List<Printer> Find()
    {
        Cups.AskNoPassword();
        var printers = new List<Printer>();
        CupsDest* dests = null;
        var count = Cups.GetDests(&dests);
        try
        {
            string defaultName = null;
            for (var index = 0; index < count; index++)
            {
                if (dests[index].IsDefault != 0)
                {
                    defaultName = TextOf(dests[index].Name);
                }
            }

            for (var index = 0; index < count; index++)
            {
                // An instance is a set of options of its own over the same printer; the default may be one.
                var name = TextOf(dests[index].Name);
                if (string.IsNullOrEmpty(name) || dests[index].Instance != null)
                {
                    continue;
                }

                var options = OptionsOf(dests[index].OptionCount, dests[index].Options);
                int.TryParse(options.GetValueOrDefault("queued-job-count"), out var jobs);
                printers.Add(new Printer(name, options.GetValueOrDefault("printer-info"), name == defaultName,
                    CupsChoices.StateOf(options.GetValueOrDefault("printer-state"),
                        options.GetValueOrDefault("printer-state-reasons")), jobs));
            }
        }
        finally
        {
            Cups.FreeDests(count, dests);
        }

        return printers;
    }

    /// <summary>Opens the printer called <paramref name="name"/>; it is asked what it can do.</summary>
    public static CupsPrinter Open(string name)
    {
        Cups.AskNoPassword();
        using var text = new CupsText(name);
        var dest = Cups.GetNamedDest(text.Pointer);
        if (dest == null)
        {
            throw Failed();
        }

        var info = Cups.CopyDestInfo(dest);
        if (info == 0)
        {
            var failure = Failed();
            Cups.FreeDests(1, dest);
            throw failure;
        }

        return new CupsPrinter(dest, info);
    }

    public CupsAnswers Ask()
    {
        var media = new List<CupsMedia>();
        var count = Cups.GetMediaCount(_dest, _info);
        for (var index = 0; index < count; index++)
        {
            CupsSize size;
            if (Cups.GetMedia(_dest, _info, index, &size) && size.Width > 0 && size.Length > 0)
            {
                media.Add(MediaOf(&size));
            }
        }

        // The user's choice for the printer, else its own; cupsGetDestMediaDefault says Letter without the first. Looked
        // up, for the list may hold the same paper under a name of its own.
        var chosen = OptionsOf(_dest->OptionCount, _dest->Options);
        var own = DefaultOf("media");
        using var name = new CupsText(chosen.GetValueOrDefault("media") ?? (own != 0 ? TextOf(Cups.GetString(own, 0)) : null));
        CupsSize start;
        var found = Cups.GetMediaByName(_dest, _info, name.Pointer, &start);
        return new CupsAnswers
        {
            Media = media,
            DefaultMedia = found ? CupsChoices.NearestTo(media, MediaOf(&start)) : null,
            DefaultSource = chosen.GetValueOrDefault("media-source") ?? DefaultOfPaper("media-source"),
            DefaultType = chosen.GetValueOrDefault("media-type") ?? DefaultOfPaper("media-type"),
            Sides = Keywords("sides"),
            ColorModes = Keywords("print-color-mode"),
            Resolutions = Resolutions(Supported("printer-resolution")),
            DefaultResolution = Resolutions(DefaultOf("printer-resolution")).FirstOrDefault(),
            Sources = Values("media-source"),
            Types = Values("media-type"),
            MaxCopies = MaxCopies(),
            Handling = Keywords("multiple-document-handling"),
        };
    }

    /// <summary><paramref name="options"/> as the system puts them right where they cannot go together; null when they
    /// can, or it does not say how.</summary>
    public Dictionary<string, string> PutRight(List<KeyValuePair<string, string>> options)
    {
        using var list = new CupsOptionList(options);
        var count = 0;
        CupsOption* resolved = null;
        var conflicts = Cups.CopyConflicts(_dest, _info, list.Count, list.Options, &count, &resolved);
        try
        {
            return conflicts > 0 && count > 0 ? OptionsOf(count, resolved) : null;
        }
        finally
        {
            Cups.FreeOptions(count, resolved);
        }
    }

    /// <summary>Sends <paramref name="pdf"/> as a job; called off, the job is canceled and nothing is printed. Returns
    /// the job's number.</summary>
    public int Print(string title, ReadOnlySpan<byte> pdf, List<KeyValuePair<string, string>> options,
        CancellationToken cancellation)
    {
        using var list = new CupsOptionList(options);
        using var name = new CupsText(title);
        using var format = new CupsText("application/pdf");
        var job = 0;
        if (Cups.CreateJob(_dest, _info, &job, name.Pointer, list.Count, list.Options) > Cups.LastSuccess)
        {
            throw Failed();
        }

        var sent = false;
        try
        {
            if (Cups.StartDocument(_dest, _info, job, name.Pointer, format.Pointer) != Cups.HttpContinue)
            {
                throw Failed();
            }

            fixed (byte* data = pdf)
            {
                for (var at = 0; at < pdf.Length; at += Chunk)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Cups.WriteRequestData(data + at, (nuint)Math.Min(Chunk, pdf.Length - at)) != Cups.HttpContinue)
                    {
                        throw Failed();
                    }
                }
            }

            cancellation.ThrowIfCancellationRequested();
            if (Cups.FinishDocument(_dest, _info) > Cups.LastSuccess)
            {
                throw Failed();
            }

            cancellation.ThrowIfCancellationRequested();
            sent = true;
            return job;
        }
        finally
        {
            if (!sent)
            {
                Cups.CancelJob(_dest, job);
            }
        }
    }

    public void Dispose()
    {
        Cups.FreeDestInfo(_info);
        Cups.FreeDests(1, _dest);
    }

    private static IOException Failed() => new(Cups.LastError ?? "The print server does not answer.");

    private CupsMedia MediaOf(CupsSize* size) => new(TextOf(size->Media), TextOf(Cups.LocalizeMedia(_dest, _info, size)),
        size->Width, size->Length, size->Left, size->Top, size->Right, size->Bottom);

    private nint Supported(string option)
    {
        using var name = new CupsText(option);
        return Cups.FindDestSupported(_dest, _info, name.Pointer);
    }

    private nint DefaultOf(string option)
    {
        using var name = new CupsText(option);
        return Cups.FindDestDefault(_dest, _info, name.Pointer);
    }

    // From media-col-default, the paper the printer starts from.
    private string DefaultOfPaper(string member)
    {
        var paper = DefaultOf("media-col");
        var members = paper != 0 ? Cups.GetCollection(paper, 0) : 0;
        if (members == 0)
        {
            return null;
        }

        using var name = new CupsText(member);
        var found = Cups.FindAttribute(members, name.Pointer);
        return found != 0 ? TextOf(Cups.GetString(found, 0)) : null;
    }

    private List<string> Keywords(string option)
    {
        var attribute = Supported(option);
        var count = attribute != 0 ? Cups.GetCount(attribute) : 0;
        var keywords = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            if (TextOf(Cups.GetString(attribute, index)) is { Length: > 0 } keyword)
            {
                keywords.Add(keyword);
            }
        }

        return keywords;
    }

    private List<CupsValue> Values(string option)
    {
        using var name = new CupsText(option);
        var values = new List<CupsValue>();
        foreach (var keyword in Keywords(option))
        {
            using var value = new CupsText(keyword);
            values.Add(new CupsValue(keyword, TextOf(Cups.LocalizeValue(_dest, _info, name.Pointer, value.Pointer))));
        }

        return values;
    }

    private static List<PrintQuality> Resolutions(nint attribute)
    {
        var count = attribute != 0 ? Cups.GetCount(attribute) : 0;
        var resolutions = new List<PrintQuality>(count);
        for (var index = 0; index < count; index++)
        {
            int down;
            int units;
            var across = Cups.GetResolution(attribute, index, &down, &units);
            if (units == Cups.PerCentimeter)
            {
                across = (int)Math.Round(across * 2.54);
                down = (int)Math.Round(down * 2.54);
            }

            if (across > 0 && down > 0)
            {
                resolutions.Add(new PrintQuality(across, down));
            }
        }

        return resolutions;
    }

    private int MaxCopies()
    {
        var attribute = Supported("copies");
        if (attribute == 0)
        {
            return 1;
        }

        int upper;
        Cups.GetRange(attribute, 0, &upper);
        return upper;
    }

    private static Dictionary<string, string> OptionsOf(int count, CupsOption* options)
    {
        var read = new Dictionary<string, string>(count);
        for (var index = 0; index < count; index++)
        {
            read[TextOf(options[index].Name)] = TextOf(options[index].Value);
        }

        return read;
    }

    private static string TextOf(byte* text) => Marshal.PtrToStringUTF8((nint)text);
}

/// <summary>A string as libcups takes it.</summary>
internal readonly unsafe struct CupsText(string text) : IDisposable
{
    private readonly nint _text = Marshal.StringToCoTaskMemUTF8(text ?? string.Empty);

    public byte* Pointer => (byte*)_text;

    public void Dispose() => Marshal.FreeCoTaskMem(_text);
}

/// <summary>A job's options as libcups takes them.</summary>
internal sealed unsafe class CupsOptionList : IDisposable
{
    private readonly CupsOption* _options;

    public CupsOptionList(IEnumerable<KeyValuePair<string, string>> options)
    {
        CupsOption* list = null;
        foreach (var (name, value) in options)
        {
            using var nameText = new CupsText(name);
            using var valueText = new CupsText(value);
            Count = Cups.AddOption(nameText.Pointer, valueText.Pointer, Count, &list);
        }

        _options = list;
    }

    public int Count { get; }

    public CupsOption* Options => _options;

    public void Dispose() => Cups.FreeOptions(Count, _options);
}
