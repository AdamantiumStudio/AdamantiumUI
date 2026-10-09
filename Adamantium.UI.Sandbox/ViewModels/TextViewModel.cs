using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Text tab: one topic at a time (<see cref="TextStand"/>), each a view of this same view-model navigated into
/// the tab's region, so what the topics share - the message, its size - stays as it was across a switch.</summary>
[ViewModel]
public partial class TextViewModel : TabPageViewModel
{
    private static readonly FontVariationList LightVariations = FontVariationList.Parse("wght=100, wdth=100");
    private static readonly FontVariationList HeavyVariations = FontVariationList.Parse("wght=1000, wdth=151");

    private readonly IRegionManager _regions;

    public TextViewModel(IRegionManager regions) : base("Text")
    {
        _regions = regions;
        ShowStand(_stand);
    }

    /// <summary>Which topic the tab is showing.</summary>
    [Bindable] private TextStand _stand = TextStand.Basics;

    partial void OnStandChanged(TextStand value) => ShowStand(value);

    private void ShowStand(TextStand stand)
        => _ = _regions.GetOrCreateRegion(RegionNames.TextStand).NavigateToInstanceAsync(this, stand.ToString());

    // Emoji from the fallback font among the letters, so selecting across them can be checked from the start.
    [Bindable, Affects(nameof(MessageLength))] private string _message = "The quick brown fox \U0001F98A jumps over the lazy dog \U0001F436 \U0001F600\U0001F389\U0001F44D";

    // Multi-line editor content: hard newlines, a line of nothing but emoji (taller than the others), and a long line to
    // show soft wrapping.
    [Bindable] private string _notes = "Multi-line editor \U0001F4DD\n\U0001F600\U0001F389\U0001F44D\U0001F30D\U0001F680\nEnter inserts a newline; Up/Down move between lines.\nThis long line has no explicit breaks so it soft-wraps at the box width when TextWrapping is on, and you can select across several lines at once \U0001F3AF.";

    [Bindable] private double _fontSize = 22;

    // Toggles the floating-label (watermark) effect on the editable TextBoxes live.
    [Bindable] private bool _floatingWatermark = true;

    // Bound by a Run's Foreground - demonstrates that a Run's color is bindable too, not just its text.
    [Bindable] private Brush _accentBrush = new SolidColorBrush("#22D3EE");

    // Fonts that draw their color glyphs as images: PNGs of Noto Color Emoji ('CBDT') and of Google's samples ('sbix').
    public FontFamily EmbeddedBitmapFont { get; } = FromFile("NotoColorEmoji.subset.ttf");

    public FontFamily StandardBitmapFont { get; } = FromFile("samples-sbix.ttf");

    // Google's samples as SVG documents ('SVG ') and, built from the same drawings, as a 'COLR' version 1 paint graph.
    public FontFamily SvgFont { get; } = FromFile("samples-picosvg.ttf");

    public FontFamily PaintFont { get; } = FromFile("samples-glyf_colr_1.ttf");

    // Google's 'COLR' version 1 test glyphs: a font with three palettes, the second for dark backgrounds.
    public FontFamily PaletteFont { get; } = FromFile("test_glyphs-glyf_colr_1.ttf");

    // Source Sans 3: dozens of character variants under names of their own, and 'aalt' offering several alternates.
    public FontFamily FeatureFont { get; } = FromFile(Path.Combine("OTFFonts", "SourceSans3-Regular.otf"));

    // The features of FeatureFont that turn a glyph into another, as a panel of OpenType features lists them.
    public IReadOnlyList<FontFeatureItem> FontFeatureItems => _fontFeatureItems ??= ListFeatures(FeatureFont.Fonts[0]);

    private IReadOnlyList<FontFeatureItem> _fontFeatureItems;

    // The character a glyph panel shows the alternates of.
    [Bindable, Affects(nameof(GlyphAlternates))] private string _alternateCharacter = "g";

    public IReadOnlyList<GlyphAlternateItem> GlyphAlternates => ListAlternates(FeatureFont.Fonts[0], AlternateCharacter);

    // Roboto Flex: optical size, slant, width and nine parametric axes, its values named by 'STAT'.
    public FontFamily VariableFont { get; } = FromFile("RobotoFlex-Variable.ttf");

    // The axes of VariableFont, as a style panel lists them.
    public IReadOnlyList<FontAxisItem> AxisItems => _axisItems ??= ListAxes(VariableFont.Fonts[0]);

    // Takes the moving line from a light, normal setting of VariableFont to a heavy, wide one and back.
    [Bindable, Affects(nameof(MovingVariations))] private bool _heavy;

    public FontVariationList MovingVariations => Heavy ? HeavyVariations : LightVariations;

    // Hebrew among Latin, with a number and brackets: each piece runs its own way.
    public string BidiSample => "Shalom is שָׁלוֹם (peace), 2026 times: שלום עולם 123!";

    [Bindable] private string _bidiEditable = "שלום world, עולם 42";

    // Arabic letters take their joined forms, lam and alef their ligature, among Latin and Arabic-Indic digits.
    public string ArabicSample => "Salaam is السلام عليكم (peace), عام ٢٠٢٦: مرحبا بالعالم 123!";

    [Bindable] private string _arabicEditable = "مرحبا world، بالعالم 42";

    // Urdu in Nastaliq: letters joined on a slant down to the baseline, by cursive attachment.
    public FontFamily NastaliqFont { get; } = FromFile("NotoNastaliqUrdu-Regular.ttf");

    public string UrduSample => "بچپن سے ہی مجھے کتابیں پڑھنے کا شوق تھا۔";

    // Church Slavonic as the synodal books print it: breathings, accents and titla over the letters, letter-titla.
    public FontFamily ChurchSlavonicFont { get; } = FromFile("PonomarUnicode.otf");

    public string ChurchSlavonicSample =>
        "Ѻ҆́ч҃е на́шъ, и҆́же є҆сѝ на нб҃сѣ́хъ, да ст҃и́тсѧ и҆́мѧ твоѐ. " +
        "Гдⷭ҇ь бг҃ъ і҆и҃съ хрⷭ҇то́съ, ҂аѱ҃і.";

    // Old Cyrillic in ustav and poluustav: the letters the modern alphabet dropped.
    public FontFamily OldCyrillicFont { get; } = FromFile("MonomakhUnicode.otf");

    public string OldCyrillicSample => "Аꙁъ ѥсмь ꙗко ꙑ ѫ ѭ ѧ ѩ ѣ ѳ ѵ ѯ ѱ ѡ ѿ ҁ ꙇ";

    // Decorative capitals of the liturgical books, the initials a chapter opens with.
    public FontFamily InitialsFont { get; } = FromFile("VertogradUnicode.otf");

    // Turns the bidi sample and its editor from left to right to right to left.
    [Bindable, Affects(nameof(BidiDirection))] private bool _rightToLeft;

    public TextDirection BidiDirection => RightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight;

    private IReadOnlyList<FontAxisItem> _axisItems;

    public int MessageLength => Message?.Length ?? 0;

    private static IReadOnlyList<FontAxisItem> ListAxes(IFont font)
    {
        return font.Axes
            .Select(axis => new FontAxisItem(axis.Tag, axis.IsHidden ? $"{axis.Name} *" : axis.Name,
                FormattableString.Invariant($"{axis.MinValue}…{axis.DefaultValue}…{axis.MaxValue}"),
                string.Join(", ", font.AxisValues
                    .Where(value => value.Values.Count == 1 && value.Values[0].Tag == axis.Tag)
                    .Select(value => value.Name)
                    .Distinct())))
            .ToArray();
    }

    private static IReadOnlyList<FontFeatureItem> ListFeatures(IFont font)
    {
        var samples = new Dictionary<string, List<string>>();
        for (var glyph = 0u; glyph < font.GlyphCount; glyph++)
        {
            var text = font.GetGlyphText(glyph).FirstOrDefault();
            if (text == null)
            {
                continue;
            }

            foreach (var alternate in font.GetGlyphAlternates(glyph))
            {
                if (!samples.TryGetValue(alternate.Feature, out var list))
                {
                    samples[alternate.Feature] = list = [];
                }

                if (list.Count < 12 && !list.Contains(text))
                {
                    list.Add(text);
                }
            }
        }

        return font.FeatureCatalog.GSUBFeatures
            .Where(feature => samples.ContainsKey(feature.Info.Tag))
            .Select(feature => new FontFeatureItem(feature.Info.Tag, feature.Name, feature.ValueCount,
                feature.SampleText ?? string.Concat(samples[feature.Info.Tag])))
            .ToArray();
    }

    private static IReadOnlyList<GlyphAlternateItem> ListAlternates(IFont font, string character)
    {
        if (string.IsNullOrEmpty(character) || (char.IsSurrogate(character[0]) && !char.IsSurrogatePair(character, 0)))
        {
            return [];
        }

        var codepoint = char.ConvertToUtf32(character, 0);
        if (!font.TryGetGlyphIndex(codepoint, out var glyph))
        {
            return [];
        }

        var text = char.ConvertFromUtf32(codepoint);
        return font.GetGlyphAlternates(glyph)
            .Select(alternate => new GlyphAlternateItem(text, alternate.Feature, alternate.Value,
                font.FeatureCatalog.GSUBFeatures.First(feature => feature.Info.Tag == alternate.Feature).Name))
            .ToArray();
    }

    private static FontFamily FromFile(string name)
    {
        return new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts", name)));
    }
}
