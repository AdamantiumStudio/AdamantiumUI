using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Platforms.Cups;

/// <summary>A printer's IPP answers as the print dialog offers them, and the dialog's choices as a job's IPP attributes.
/// </summary>
internal sealed class CupsChoices
{
    private const double DipsPerUnit = 96 / 2540.0;
    private const int SameSize = 50;   // half a millimeter: drivers round a paper to their own dots

    public const string Media = "media";
    public const string MediaCollection = "media-col";
    public const string Source = "media-source";
    public const string Type = "media-type";
    public const string Sides = "sides";
    public const string ColorMode = "print-color-mode";
    public const string Resolution = "printer-resolution";
    public const string Copies = "copies";
    public const string Handling = "multiple-document-handling";
    public const string Scaling = "print-scaling";

    private const string Auto = "auto";
    private const string Collated ="separate-documents-collated-copies";
    private const string Uncollated = "separate-documents-uncollated-copies";

    private static readonly Dictionary<DuplexMode, string> SidesOf = new()
    {
        [DuplexMode.OneSided] = "one-sided",
        [DuplexMode.LongEdge] = "two-sided-long-edge",
        [DuplexMode.ShortEdge] = "two-sided-short-edge",
    };

    private readonly Dictionary<string, CupsMedia> _papers = new();
    private readonly Dictionary<string, string> _trays = new();
    private readonly Dictionary<string, string> _types = new();
    private readonly CupsAnswers _answers;

    public CupsChoices(CupsAnswers answers)
    {
        _answers = answers;
        var papers = new List<PaperSize>();
        var margins = new Dictionary<string, Thickness>();
        PaperSize defaultPaper = null;

        // The default first. Of a paper given twice, with margins and borderless, the one with margins is meant: printing
        // to the edge is asked for, not slipped in.
        foreach (var media in answers.Media.OrderByDescending(media => media == answers.DefaultMedia)
                     .ThenBy(media => media.IsBorderless))
        {
            var paper = PaperOf(media);
            if (!_papers.TryAdd(paper.Name, media))
            {
                continue;
            }

            papers.Add(paper);
            margins[paper.Name] = new Thickness(media.Left * DipsPerUnit, media.Top * DipsPerUnit,
                media.Right * DipsPerUnit, media.Bottom * DipsPerUnit);
            if (media == answers.DefaultMedia)
            {
                defaultPaper = paper;
            }
        }

        Offer(_trays, answers.Sources, answers.DefaultSource);
        Offer(_types, answers.Types, answers.DefaultType);
        Capabilities = new PrinterCapabilities
        {
            Papers = papers.Count > 0 ? papers : PaperSize.Standard,
            DefaultPaper = defaultPaper ?? (papers.Count > 0 ? papers[0] : PaperSize.A4),
            Margins = margins,
            MaxCopies = Math.Max(1, answers.MaxCopies),
            Collates = answers.Handling.Contains(Collated) && answers.Handling.Contains(Uncollated),
            Duplex = SidesOf.Where(side => answers.Sides.Contains(side.Value)).Select(side => side.Key).ToList(),
            Colors = ColorsOf(answers),
            Qualities = answers.Resolutions,
            DefaultQuality = answers.DefaultResolution,
            Trays = _trays.Keys.ToList(),
            MediaTypes = _types.Keys.ToList(),
        };
    }

    public PrinterCapabilities Capabilities { get; }

    /// <summary>The job's attributes for <paramref name="settings"/>: only what the printer offers a choice of. The
    /// sheets are laid out already, so it does not scale them.</summary>
    public List<KeyValuePair<string, string>> Write(PrintSettings settings) => Write(settings, false);

    /// <summary>The same, the paper's source and type each on its own, as a printer's constraints name them.</summary>
    public List<KeyValuePair<string, string>> WriteToCheck(PrintSettings settings) => Write(settings, true);

    private List<KeyValuePair<string, string>> Write(PrintSettings settings, bool toCheck)
    {
        var options = new List<KeyValuePair<string, string>>();
        if (settings.Paper != null && _papers.TryGetValue(settings.Paper.Name, out var media))
        {
            // Left as the printer has them, they are not asked for.
            var tray = settings.Tray != null ? _trays.GetValueOrDefault(settings.Tray) : null;
            var type = settings.MediaType != null ? _types.GetValueOrDefault(settings.MediaType) : null;
            if (toCheck)
            {
                Add(Media, media.Keyword);
                if (tray != null)
                {
                    Add(Source, tray);
                }

                if (type != null)
                {
                    Add(Type, type);
                }
            }
            else if ((tray == null || tray == _answers.DefaultSource) && (type == null || type == _answers.DefaultType))
            {
                Add(Media, media.Keyword);
            }
            else
            {
                Add(MediaCollection, Collection(media, tray, type));
            }
        }

        if (Capabilities.Duplex.Contains(settings.Duplex))
        {
            Add(Sides, SidesOf[settings.Duplex]);
        }

        if (Capabilities.Colors.Contains(settings.Color))
        {
            Add(ColorMode, settings.Color == PrintColorMode.Color ? "color" : GrayOf(_answers));
        }

        if (settings.Quality != null && Capabilities.Qualities.Contains(settings.Quality))
        {
            Add(Resolution, TextOf(settings.Quality));
        }

        if (settings.Copies > 1)
        {
            Add(Copies, Text(Math.Min(settings.Copies, Capabilities.MaxCopies)));
            if (Capabilities.Collates)
            {
                Add(Handling, settings.Collate ? Collated : Uncollated);
            }
        }

        Add(Scaling, "none");
        return options;

        void Add(string name, string value) => options.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary><paramref name="settings"/> with what the system put right, as <paramref name="options"/> say.</summary>
    public PrintSettings ReadBack(IEnumerable<KeyValuePair<string, string>> options, PrintSettings settings)
    {
        foreach (var (name, value) in options)
        {
            switch (name)
            {
                case Media when _papers.FirstOrDefault(paper => paper.Value.Keyword == value).Key is { } paperName:
                    settings = settings with { Paper = Capabilities.Papers.First(paper => paper.Name == paperName) };
                    break;
                case Source when _trays.FirstOrDefault(tray => tray.Value == value).Key is { } tray:
                    settings = settings with { Tray = tray };
                    break;
                case Type when _types.FirstOrDefault(type => type.Value == value).Key is { } type:
                    settings = settings with { MediaType = type };
                    break;
                case Sides when SidesOf.ContainsValue(value):
                    settings = settings with { Duplex = SidesOf.First(side => side.Value == value).Key };
                    break;
                case ColorMode:
                    settings = settings with
                    {
                        Color = IsGray(value) ? PrintColorMode.Grayscale : PrintColorMode.Color
                    };
                    break;
                case Resolution when Capabilities.Qualities.FirstOrDefault(quality => TextOf(quality) == value) is { } found:
                    settings = settings with { Quality = found };
                    break;
                case Copies when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var copies):
                    settings = settings with { Copies = Math.Max(1, copies) };
                    break;
                case Handling:
                    settings = settings with { Collate = value != Uncollated };
                    break;
            }
        }

        return settings;
    }

    /// <summary>The state the dialog says, from printer-state (3 idle, 4 processing, 5 stopped) and its reasons: what
    /// holds the printer up first. Reports and warnings - paper getting low - hold nothing up.</summary>
    public static PrinterState StateOf(string state, string reasons)
    {
        var all = (reasons ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var held = all.Where(reason => !reason.EndsWith("-report", StringComparison.Ordinal) &&
                                       !reason.EndsWith("-warning", StringComparison.Ordinal)).ToList();

        // Backends report a printer switched off; one being connected to at the start of a job is at work.
        if (all.Contains("offline-report") || Has("offline") || Has("shutdown") || Has("timed-out") ||
            Has("connecting-to-device") && state != "4")
        {
            return PrinterState.Offline;
        }

        if (Has("media-empty") || Has("media-needed"))
        {
            return PrinterState.OutOfPaper;
        }

        if (Has("media-jam"))
        {
            return PrinterState.PaperJam;
        }

        if (Has("paused") || Has("moving-to-paused"))
        {
            return PrinterState.Paused;
        }

        if (Has("cover-open") || Has("door-open") || Has("toner-empty") || Has("marker-supply-empty") ||
            Has("cups-missing-filter") || Has("cups-insecure-filter"))
        {
            return PrinterState.Error;
        }

        return state switch
        {
            "4" => PrinterState.Printing,
            "5" => PrinterState.Paused,
            _ => PrinterState.Ready,
        };

        bool Has(string reason) => held.Any(name => name == reason || name == reason + "-error");
    }

    /// <summary><paramref name="wanted"/> among <paramref name="papers"/>: the same, else of its size and with margins;
    /// null where there is none.</summary>
    public static CupsMedia NearestTo(IReadOnlyList<CupsMedia> papers, CupsMedia wanted) =>
        papers.FirstOrDefault(paper => paper with { Title = wanted.Title } == wanted && !paper.IsBorderless) ??
        papers.Where(paper => Math.Abs(paper.Width - wanted.Width) <= SameSize &&
                              Math.Abs(paper.Length - wanted.Length) <= SameSize)
            .OrderBy(paper => paper.IsBorderless).FirstOrDefault();

    /// <summary>A paper in words, from the name PWG 5101.1 gives it: its class, its name and its size -
    /// "na_number-10_4.125x9.5in" is "Number 10".</summary>
    private static string NameOf(string keyword)
    {
        var parts = keyword.Split('_');
        if (parts.Length < 3)
        {
            return keyword;
        }

        if (parts[0] == "custom")
        {
            return parts[2].Replace("x", " × ").Replace("mm", " mm").Replace("in", " in");
        }

        var name = parts[1];
        if (parts[0] is "iso" or "jis" && name.Length <= 4)
        {
            return parts[0] == "jis" ? "JIS " + name.ToUpperInvariant() : name.ToUpperInvariant();
        }

        return InWords(name);
    }

    private static PaperSize PaperOf(CupsMedia media)
    {
        foreach (var standard in PaperSize.Standard)
        {
            if (Math.Abs(standard.Width / DipsPerUnit - media.Width) <= SameSize &&
                Math.Abs(standard.Height / DipsPerUnit - media.Length) <= SameSize)
            {
                return standard;
            }
        }

        return new PaperSize(string.IsNullOrEmpty(media.Title) ? NameOf(media.Keyword) : media.Title, media.Width * DipsPerUnit,
            media.Length * DipsPerUnit);
    }

    private static IReadOnlyList<PrintColorMode> ColorsOf(CupsAnswers answers)
    {
        var colors = new List<PrintColorMode>();
        if (answers.ColorModes.Contains("color"))
        {
            colors.Add(PrintColorMode.Color);
        }

        if (GrayOf(answers) != null)
        {
            colors.Add(PrintColorMode.Grayscale);
        }

        return colors;
    }

    private static string GrayOf(CupsAnswers answers) =>
        answers.ColorModes.Contains("monochrome") ? "monochrome" : answers.ColorModes.FirstOrDefault(IsGray);

    private static bool IsGray(string mode) =>
        mode.Contains("monochrome", StringComparison.Ordinal) || mode == "bi-level";

    /// <summary>By name, what the printer does unless told otherwise first - its default, else "auto", else a choice to
    /// leave it to the printer; of two called the same, the first.</summary>
    private static void Offer(Dictionary<string, string> offered, IReadOnlyList<CupsValue> values, string own)
    {
        if (values.Count > 1 && values.All(value => value.Keyword != own && value.Keyword != Auto))
        {
            offered.Add(InWords(Auto), null);
        }

        foreach (var value in values.OrderByDescending(value => value.Keyword == own)
                     .ThenByDescending(value => value.Keyword == Auto))
        {
            offered.TryAdd(NameOf(value), value.Keyword);
        }
    }

    /// <summary>As the printer names it; one that gives no name is called by its keyword, in words.</summary>
    private static string NameOf(CupsValue value) =>
        string.IsNullOrEmpty(value.Title) || value.Title == value.Keyword ? InWords(value.Keyword) : value.Title;

    private static string InWords(string keyword)
    {
        var words = keyword.Replace('-', ' ');
        return words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : words;
    }

    private static string Collection(CupsMedia media, string tray, string type)
    {
        var text = "{media-size={x-dimension=" + Text(media.Width) + " y-dimension=" + Text(media.Length) + "}" +
                   " media-left-margin=" + Text(media.Left) + " media-top-margin=" + Text(media.Top) +
                   " media-right-margin=" + Text(media.Right) + " media-bottom-margin=" + Text(media.Bottom);
        if (tray != null)
        {
            text += " media-source=" + Quoted(tray);
        }

        return type != null ? text + " media-type=" + Quoted(type) + "}" : text + "}";
    }

    /// <summary>A value as the options' text takes it: a name of the administrator's own may hold anything.</summary>
    private static string Quoted(string value) =>
        value.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '.')
            ? value
            : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string TextOf(PrintQuality quality) => quality.DotsPerInchX == quality.DotsPerInchY
        ? Text(quality.DotsPerInchX) + "dpi"
        : Text(quality.DotsPerInchX) + "x" + Text(quality.DotsPerInchY) + "dpi";

    private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);
}
