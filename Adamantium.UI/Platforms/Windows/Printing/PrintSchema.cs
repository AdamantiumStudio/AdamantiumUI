using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>The Print Schema (the XML Windows describes a printer and a job in): a printer's PrintCapabilities read into
/// <see cref="PrinterCapabilities"/>, and the dialog's choices written into its PrintTicket and read back out of one.
/// Each choice keeps the option it came from, so a ticket names exactly what the driver offered.</summary>
internal sealed class PrintSchema
{
    public static readonly XNamespace Framework = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
    public static readonly XNamespace Keywords = "http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords";
    private static readonly XNamespace Instance = "http://www.w3.org/2001/XMLSchema-instance";

    private const double DipsPerMicron = 96 / 25400.0;

    // The features of a job the dialog offers, in the names drivers give them (several for some: a driver uses one).
    private static readonly XName[] CopiesNames = [Keywords + "JobCopiesAllDocuments", Keywords + "DocumentCopiesAllPages"];
    private static readonly XName[] CollateNames = [Keywords + "DocumentCollate", Keywords + "JobCollateAllDocuments"];
    private static readonly XName[] DuplexNames = [Keywords + "JobDuplexAllDocumentsContiguously", Keywords + "DocumentDuplex"];
    private static readonly XName[] TrayNames = [Keywords + "JobInputBin", Keywords + "DocumentInputBin", Keywords + "PageInputBin"];
    private static readonly XName PaperName = Keywords + "PageMediaSize";
    private static readonly XName OrientationName = Keywords + "PageOrientation";
    private static readonly XName ColorName = Keywords + "PageOutputColor";
    private static readonly XName QualityName = Keywords + "PageResolution";
    private static readonly XName MediaTypeName = Keywords + "PageMediaType";

    private readonly Dictionary<string, XElement> _papers = new();
    private readonly Dictionary<DuplexMode, XElement> _duplex = new();
    private readonly Dictionary<PrintColorMode, XElement> _colors = new();
    private readonly Dictionary<PrintQuality, XElement> _qualities = new();
    private readonly Dictionary<string, XElement> _trays = new();
    private readonly Dictionary<string, XElement> _mediaTypes = new();
    private XElement _collated;
    private XElement _uncollated;
    private XName _copies;
    private XName _collate;
    private XName _duplexFeature;
    private XName _trayFeature;

    private PrintSchema()
    {
    }

    /// <summary>What the printer can do, as its PrintCapabilities said.</summary>
    public PrinterCapabilities Capabilities { get; private set; } = PrinterCapabilities.Pdf;

    /// <summary>Reads <paramref name="capabilities"/>; the selections of <paramref name="defaults"/> - the printer's
    /// default ticket - are what it starts on.</summary>
    public static PrintSchema Read(XDocument capabilities, XDocument defaults,
        IReadOnlyDictionary<string, Thickness> margins = null, bool hasDriverSettings = false)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var schema = new PrintSchema();
        var root = capabilities.Root!;
        var papers = new List<PaperSize>();
        foreach (var option in Options(root, PaperName))
        {
            var width = Scored(option, "MediaSizeWidth");
            var height = Scored(option, "MediaSizeHeight");
            if (width is not > 0 || height is not > 0)
            {
                continue;
            }

            var paper = PaperOf(option, width.Value, height.Value);
            if (schema._papers.TryAdd(paper.Name, option))
            {
                papers.Add(paper);
            }
        }

        var copies = CopiesNames.FirstOrDefault(name => Parameter(root, name) != null);
        schema._copies = copies;
        var maxCopies = copies != null ? (int)(Number(Parameter(root, copies), "MaxValue") ?? 1) : 1;

        schema._collate = CollateNames.FirstOrDefault(name => Feature(root, name) != null);
        if (schema._collate != null)
        {
            schema._collated = Option(root, schema._collate, "Collated");
            schema._uncollated = Option(root, schema._collate, "Uncollated");
        }

        schema._duplexFeature = DuplexNames.FirstOrDefault(name => Feature(root, name) != null);
        if (schema._duplexFeature != null)
        {
            Offer(schema._duplex, DuplexMode.OneSided, Option(root, schema._duplexFeature, "OneSided"));
            Offer(schema._duplex, DuplexMode.LongEdge, Option(root, schema._duplexFeature, "TwoSidedLongEdge"));
            Offer(schema._duplex, DuplexMode.ShortEdge, Option(root, schema._duplexFeature, "TwoSidedShortEdge"));
        }

        Offer(schema._colors, PrintColorMode.Color, Option(root, ColorName, "Color"));
        Offer(schema._colors, PrintColorMode.Grayscale,
            Option(root, ColorName, "Grayscale") ?? Option(root, ColorName, "Monochrome"));

        foreach (var option in Options(root, QualityName))
        {
            var x = Scored(option, "ResolutionX");
            var y = Scored(option, "ResolutionY") ?? x;
            if (x is > 0 && y is > 0)
            {
                schema._qualities.TryAdd(new PrintQuality((int)x.Value, (int)y.Value), option);
            }
        }

        schema._trayFeature = TrayNames.FirstOrDefault(name => Feature(root, name) != null);
        if (schema._trayFeature != null)
        {
            foreach (var option in Options(root, schema._trayFeature))
            {
                schema._trays.TryAdd(DisplayName(option), option);
            }
        }

        foreach (var option in Options(root, MediaTypeName))
        {
            schema._mediaTypes.TryAdd(DisplayName(option), option);
        }

        var start = defaults?.Root;
        var defaultPaper = start != null ? schema.Selected(start, PaperName, schema._papers) : null;
        var defaultQuality = start != null ? schema.Selected(start, QualityName, schema._qualities) : null;
        schema.Capabilities = new PrinterCapabilities
        {
            Papers = papers.Count > 0 ? papers : PaperSize.Standard,
            DefaultPaper = papers.FirstOrDefault(paper => paper.Name == defaultPaper) ?? papers.FirstOrDefault(),
            MaxCopies = Math.Max(1, maxCopies),
            Collates = schema._collated != null && schema._uncollated != null,
            Duplex = [.. schema._duplex.Keys.Order()],
            Colors = [.. schema._colors.Keys.Order()],
            Qualities = [.. schema._qualities.Keys.OrderBy(quality => quality.DotsPerInchX * quality.DotsPerInchY)],
            DefaultQuality = defaultQuality ?? schema._qualities.Keys.FirstOrDefault(),
            Trays = [.. schema._trays.Keys],
            MediaTypes = [.. schema._mediaTypes.Keys],
            Margins = margins ?? new Dictionary<string, Thickness>(),
            HasDriverSettings = hasDriverSettings,
        };
        return schema;
    }

    /// <summary>The paper of <paramref name="option"/> - named as the standard one of its size, so the user's A4 stays
    /// A4 from printer to printer; otherwise by the driver's own name.</summary>
    private static PaperSize PaperOf(XElement option, double widthMicrons, double heightMicrons)
    {
        var width = widthMicrons * DipsPerMicron;
        var height = heightMicrons * DipsPerMicron;
        const double Tolerance = 96 / 25.4; // a millimeter
        var standard = PaperSize.Standard.FirstOrDefault(paper =>
            Math.Abs(paper.Width - width) < Tolerance && Math.Abs(paper.Height - height) < Tolerance);
        return standard ?? new PaperSize(DisplayName(option), width, height);
    }

    /// <summary>The margins of <paramref name="paper"/> as the PageImageableSize of capabilities asked with it chosen
    /// say; null when they do not say.</summary>
    public static Thickness? MarginsIn(XDocument capabilities, PaperSize paper)
    {
        var imageable = capabilities.Root?.Elements(Framework + "Property")
            .FirstOrDefault(property => Is(property, Keywords + "PageImageableSize"));
        var area = imageable?.Elements(Framework + "Property").FirstOrDefault(p => Is(p, Keywords + "ImageableArea"));
        if (area == null || paper == null)
        {
            return null;
        }

        var left = Number(area, "OriginWidth") ?? 0;
        var top = Number(area, "OriginHeight") ?? 0;
        var width = Number(area, "ExtentWidth");
        var height = Number(area, "ExtentHeight");
        if (width is not > 0 || height is not > 0)
        {
            return null;
        }

        var paperWidth = paper.Width / DipsPerMicron;
        var paperHeight = paper.Height / DipsPerMicron;
        return new Thickness(left * DipsPerMicron, top * DipsPerMicron,
            Math.Max(0, paperWidth - left - width.Value) * DipsPerMicron,
            Math.Max(0, paperHeight - top - height.Value) * DipsPerMicron);
    }

    /// <summary><paramref name="ticket"/> with <paramref name="settings"/>' choices in it - each one the driver offered;
    /// the rest of the ticket - the driver's own settings among it - as it was.</summary>
    public XDocument Write(XDocument ticket, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(settings);
        var result = new XDocument(ticket);
        var root = result.Root!;
        if (settings.Paper != null && _papers.TryGetValue(settings.Paper.Name, out var paper))
        {
            Choose(root, PaperName, paper);
        }

        Choose(root, OrientationName,
            Keywords + (settings.Orientation == PageOrientation.Landscape ? "Landscape" : "Portrait"), null);
        if (_copies != null)
        {
            SetParameter(root, _copies, Math.Max(1, settings.Copies));
        }

        if (_collate != null && (settings.Collate ? _collated : _uncollated) is { } collate)
        {
            Choose(root, _collate, collate);
        }

        if (_duplexFeature != null && _duplex.TryGetValue(settings.Duplex, out var duplex))
        {
            Choose(root, _duplexFeature, duplex);
        }

        if (_colors.TryGetValue(settings.Color, out var color))
        {
            Choose(root, ColorName, color);
        }

        if (settings.Quality != null && _qualities.TryGetValue(settings.Quality, out var quality))
        {
            Choose(root, QualityName, quality);
        }

        if (_trayFeature != null && settings.Tray != null && _trays.TryGetValue(settings.Tray, out var tray))
        {
            Choose(root, _trayFeature, tray);
        }

        if (settings.MediaType != null && _mediaTypes.TryGetValue(settings.MediaType, out var mediaType))
        {
            Choose(root, MediaTypeName, mediaType);
        }

        return result;
    }

    /// <summary><paramref name="settings"/> with the choices <paramref name="ticket"/> - as the driver put it right -
    /// selects; what it does not name stays as it was.</summary>
    public PrintSettings ReadBack(XDocument ticket, PrintSettings settings)
    {
        var root = ticket?.Root;
        if (root == null)
        {
            return settings;
        }

        var paper = Selected(root, PaperName, _papers);
        var copies = _copies != null ? ParameterValue(root, _copies) : null;
        var collate = _collate != null ? SelectedName(root, _collate) : null;
        var duplex = _duplexFeature != null ? Selected(root, _duplexFeature, _duplex) : (DuplexMode?)null;
        var color = Selected(root, ColorName, _colors);
        var quality = Selected(root, QualityName, _qualities);
        var tray = _trayFeature != null ? Selected(root, _trayFeature, _trays) : null;
        var mediaType = Selected(root, MediaTypeName, _mediaTypes);
        return settings with
        {
            Paper = paper != null ? Capabilities.Papers.FirstOrDefault(p => p.Name == paper) ?? settings.Paper : settings.Paper,
            Copies = copies is > 0 ? (int)copies.Value : settings.Copies,
            Collate = collate != null && _collated != null
                ? collate == NameOf(_collated)
                : settings.Collate,
            Duplex = duplex ?? settings.Duplex,
            Color = color ?? settings.Color,
            Quality = quality ?? settings.Quality,
            Tray = tray ?? settings.Tray,
            MediaType = mediaType ?? settings.MediaType,
        };
    }

    private static void Offer<TKey>(Dictionary<TKey, XElement> into, TKey key, XElement option)
    {
        if (option != null)
        {
            into.TryAdd(key, option);
        }
    }

    private TKey Selected<TKey>(XElement root, XName feature, Dictionary<TKey, XElement> options)
    {
        var chosen = SelectedName(root, feature);
        if (chosen == null)
        {
            return default;
        }

        foreach (var pair in options)
        {
            if (NameOf(pair.Value) == chosen)
            {
                return pair.Key;
            }
        }

        return default;
    }

    private DuplexMode? Selected(XElement root, XName feature, Dictionary<DuplexMode, XElement> options)
    {
        var chosen = SelectedName(root, feature);
        foreach (var pair in options)
        {
            if (NameOf(pair.Value) == chosen)
            {
                return pair.Key;
            }
        }

        return null;
    }

    private PrintColorMode? Selected(XElement root, XName feature, Dictionary<PrintColorMode, XElement> options)
    {
        var chosen = SelectedName(root, feature);
        foreach (var pair in options)
        {
            if (NameOf(pair.Value) == chosen)
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static XName SelectedName(XElement root, XName feature) =>
        Feature(root, feature)?.Element(Framework + "Option") is { } option ? NameOf(option) : null;

    /// <summary>Chooses <paramref name="option"/> - one the capabilities offered - in <paramref name="feature"/>.
    /// </summary>
    private static void Choose(XElement root, XName feature, XElement option) =>
        Choose(root, feature, NameOf(option), option.Elements(Framework + "ScoredProperty"));

    /// <summary>Chooses the option named <paramref name="option"/> in <paramref name="feature"/>, with its own values
    /// - a paper's width and height - but not its display name.</summary>
    private static void Choose(XElement root, XName feature, XName option, IEnumerable<XElement> scored)
    {
        var existing = Feature(root, feature);
        if (existing == null)
        {
            existing = new XElement(Framework + "Feature", new XAttribute("name", Prefixed(root, feature)));
            root.Add(existing);
        }

        existing.Elements(Framework + "Option").Remove();
        var copy = new XElement(Framework + "Option", new XAttribute("name", Prefixed(root, option)));
        foreach (var property in scored ?? [])
        {
            copy.Add(Requalified(root, property));
        }

        existing.Add(copy);
    }

    private static void SetParameter(XElement root, XName parameter, int value)
    {
        var existing = root.Elements(Framework + "ParameterInit").FirstOrDefault(element => Is(element, parameter));
        if (existing == null)
        {
            existing = new XElement(Framework + "ParameterInit", new XAttribute("name", Prefixed(root, parameter)));
            root.Add(existing);
        }

        existing.RemoveNodes();
        existing.Add(new XElement(Framework + "Value", new XAttribute(Instance + "type", Prefixed(root,
            XNamespace.Get("http://www.w3.org/2001/XMLSchema") + "integer")), value.ToString(CultureInfo.InvariantCulture)));
    }

    private static double? ParameterValue(XElement root, XName parameter) =>
        root.Elements(Framework + "ParameterInit").FirstOrDefault(element => Is(element, parameter))?
            .Element(Framework + "Value") is { } value && double.TryParse(value.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>A copy of <paramref name="element"/> whose QName values (names, types) are written with the prefixes
    /// the ticket declares.</summary>
    private static XElement Requalified(XElement root, XElement element)
    {
        var copy = new XElement(element.Name);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var qualified = attribute.Name.LocalName is "name" || attribute.Name == Instance + "type"
                ? Resolve(element, attribute.Value)
                : null;
            copy.Add(qualified != null
                ? new XAttribute(attribute.Name, Prefixed(root, qualified))
                : new XAttribute(attribute));
        }

        foreach (var node in element.Nodes())
        {
            copy.Add(node is XElement child ? Requalified(root, child) : node);
        }

        return copy;
    }

    /// <summary><paramref name="name"/> as "prefix:local" with a prefix <paramref name="root"/> declares - declared on
    /// it first if none does.</summary>
    private static string Prefixed(XElement root, XName name)
    {
        var prefix = root.GetPrefixOfNamespace(name.Namespace);
        if (prefix == null)
        {
            var index = 0;
            do
            {
                prefix = "ns" + index.ToString("D4", CultureInfo.InvariantCulture) + "a";
                index++;
            } while (root.GetNamespaceOfPrefix(prefix) != null);

            root.Add(new XAttribute(XNamespace.Xmlns + prefix, name.NamespaceName));
        }

        return prefix + ":" + name.LocalName;
    }

    private static XName Resolve(XElement scope, string qualified)
    {
        var colon = qualified.IndexOf(':');
        if (colon < 0)
        {
            return qualified;
        }

        // A prefix the document does not declare names nothing it knows.
        var space = scope.GetNamespaceOfPrefix(qualified[..colon]);
        return space != null ? space + qualified[(colon + 1)..] : null;
    }

    private static XName NameOf(XElement element) =>
        element.Attribute("name") is { } name ? Resolve(element, name.Value) : null;

    private static bool Is(XElement element, XName name) => NameOf(element) == name;

    private static XElement Feature(XElement root, XName name) =>
        root.Elements(Framework + "Feature").FirstOrDefault(feature => Is(feature, name));

    private static XElement Parameter(XElement root, XName name) =>
        root.Elements(Framework + "ParameterDef").FirstOrDefault(parameter => Is(parameter, name));

    private static IEnumerable<XElement> Options(XElement root, XName feature) =>
        Feature(root, feature)?.Elements(Framework + "Option") ?? [];

    private static XElement Option(XElement root, XName feature, string keyword) =>
        Options(root, feature).FirstOrDefault(option => NameOf(option) == Keywords + keyword);

    private static double? Scored(XElement option, string keyword) =>
        option.Elements(Framework + "ScoredProperty").FirstOrDefault(p => Is(p, Keywords + keyword))?
            .Element(Framework + "Value") is { } value && double.TryParse(value.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>The number in property <paramref name="keyword"/> of <paramref name="parent"/> - a Print Schema keyword
    /// or, for a parameter's bounds, a framework one.</summary>
    private static double? Number(XElement parent, string keyword)
    {
        var property = parent?.Elements(Framework + "Property")
            .FirstOrDefault(p => Is(p, Keywords + keyword) || Is(p, Framework + keyword));
        return property?.Element(Framework + "Value") is { } value && double.TryParse(value.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string DisplayName(XElement option)
    {
        var display = option.Elements(Framework + "Property").FirstOrDefault(p => Is(p, Keywords + "DisplayName"))?
            .Element(Framework + "Value")?.Value;
        return !string.IsNullOrWhiteSpace(display) ? display.Trim() : NameOf(option)?.LocalName ?? "?";
    }
}
