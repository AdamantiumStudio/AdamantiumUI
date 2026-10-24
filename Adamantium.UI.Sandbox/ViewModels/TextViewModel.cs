using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Graphics.Fonts;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Sandbox.Content;

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

    partial void OnStandChanged(TextStand value)
    {
        WatchTheAtlas(value == TextStand.Typesetting);
        ShowStand(value);
    }

    private void ShowStand(TextStand stand)
        => _ = _regions.GetOrCreateRegion(RegionNames.TextStand).NavigateToInstanceAsync(this, stand.ToString());

    // Emoji from the fallback font among the letters, so selecting across them can be checked from the start.
    [Bindable, Affects(nameof(MessageLength))] private string _message = "The quick brown fox \U0001F98A jumps over the lazy dog \U0001F436 \U0001F600\U0001F389\U0001F44D";

    /// <summary>A name the view-model checks, for the field that marks itself while it is wrong.</summary>
    public ProjectNameCheck NameCheck { get; } = new();

    // Multi-line editor content: hard newlines, a line of nothing but emoji (taller than the others), and a long line to
    // show soft wrapping.
    [Bindable] private string _notes = "Multi-line editor \U0001F4DD\n\U0001F600\U0001F389\U0001F44D\U0001F30D\U0001F680\nEnter inserts a newline; Up/Down move between lines.\nThis long line has no explicit breaks so it soft-wraps at the box width when TextWrapping is on, and you can select across several lines at once \U0001F3AF.";

    [Bindable] private double _fontSize = 22;

    // Toggles the floating-label (watermark) effect on the editable TextBoxes live.
    [Bindable] private bool _floatingWatermark = true;

    // A draft with a few misspelled words, for the box that checks its spelling.
    [Bindable] private string _draft = "Thsi sentense has a fiew misteaks in it.\nTab\tworks here too.";

    // The case the message is shown in on the editing stand; the message itself keeps its own.
    [Bindable] private TextTransform _shownCase = TextTransform.Uppercase;

    // Bound two-way by both password boxes on the editing stand: typing in one shows in the other.
    [Bindable, Affects(nameof(PasswordLength))] private string _password = string.Empty;

    public int PasswordLength => new System.Globalization.StringInfo(Password ?? string.Empty).LengthInTextElements;

    // Bound by a Run's Foreground - demonstrates that a Run's color is bindable too, not just its text.
    [Bindable] private Brush _accentBrush = new SolidColorBrush("#22D3EE");

    // Fonts that draw their color glyphs as images: PNGs of Noto Color Emoji ('CBDT') and of Google's samples ('sbix').
    public FontFamily EmbeddedBitmapFont { get; } = FromFile(SandboxContentAssets.Fonts.NotoColorEmoji_subset);

    public FontFamily StandardBitmapFont { get; } = FromFile(SandboxContentAssets.Fonts.Samples_sbix);

    // Google's samples as SVG documents ('SVG ') and, built from the same drawings, as a 'COLR' version 1 paint graph.
    public FontFamily SvgFont { get; } = FromFile(SandboxContentAssets.Fonts.Samples_picosvg);

    public FontFamily PaintFont { get; } = FromFile(SandboxContentAssets.Fonts.Samples_glyf_colr_1);

    // Google's 'COLR' version 1 test glyphs: a font with three palettes, the second for dark backgrounds.
    public FontFamily PaletteFont { get; } = FromFile(SandboxContentAssets.Fonts.Test_glyphs_glyf_colr_1);

    // Source Sans 3: dozens of character variants under names of their own, and 'aalt' offering several alternates.
    public FontFamily FeatureFont { get; } = FromFile(SandboxContentAssets.Fonts.OTFFonts.SourceSans3_Regular);

    // The features of FeatureFont that turn a glyph into another, as a panel of OpenType features lists them.
    public IReadOnlyList<FontFeatureItem> FontFeatureItems => _fontFeatureItems ??= ListFeatures(FeatureFont.Fonts[0]);

    private IReadOnlyList<FontFeatureItem> _fontFeatureItems;

    // The character a glyph panel shows the alternates of.
    [Bindable, Affects(nameof(GlyphAlternates))] private string _alternateCharacter = "g";

    public IReadOnlyList<GlyphAlternateItem> GlyphAlternates => ListAlternates(FeatureFont.Fonts[0], AlternateCharacter);

    // Roboto Flex: optical size, slant, width and nine parametric axes, its values named by 'STAT'.
    public FontFamily VariableFont { get; } = FromFile(SandboxContentAssets.Fonts.RobotoFlex_Variable);

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
    public FontFamily NastaliqFont { get; } = FromFile(SandboxContentAssets.Fonts.NotoNastaliqUrdu_Regular);

    public string UrduSample => "بچپن سے ہی مجھے کتابیں پڑھنے کا شوق تھا۔";

    // Church Slavonic as the synodal books print it: breathings, accents and titla over the letters, letter-titla.
    public FontFamily ChurchSlavonicFont { get; } = FromFile(SandboxContentAssets.Fonts.PonomarUnicode);

    public string ChurchSlavonicSample =>
        "Ѻ҆́ч҃е на́шъ, и҆́же є҆сѝ на нб҃сѣ́хъ, да ст҃и́тсѧ и҆́мѧ твоѐ. " +
        "Гдⷭ҇ь бг҃ъ і҆и҃съ хрⷭ҇то́съ, ҂аѱ҃і.";

    // Old Cyrillic in ustav and poluustav: the letters the modern alphabet dropped.
    public FontFamily OldCyrillicFont { get; } = FromFile(SandboxContentAssets.Fonts.MonomakhUnicode);

    public string OldCyrillicSample => "Аꙁъ ѥсмь ꙗко ꙑ ѫ ѭ ѧ ѩ ѣ ѳ ѵ ѯ ѱ ѡ ѿ ҁ ꙇ";

    // Decorative capitals of the liturgical books, the initials a chapter opens with.
    public FontFamily InitialsFont { get; } = FromFile(SandboxContentAssets.Fonts.VertogradUnicode);

    // Turns the bidi sample and its editor from left to right to right to left.
    [Bindable, Affects(nameof(BidiDirection))] private bool _rightToLeft;

    public TextDirection BidiDirection => RightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight;

    // How the columns of the paragraph topic break into lines and their words, how wide they are, and whether they are
    // justified.
    [Bindable] private LineBreaking _lineBreaking = LineBreaking.Paragraph;

    [Bindable] private Hyphens _hyphens = Hyphens.Auto;

    [Bindable] private bool _opticalMargins = true;

    // "I Am a Cat" set vertically: kana and ideographs upright, Latin turned, the year sideways and short numbers upright
    // across the line.
    public string VerticalSample =>
        "「吾輩は猫である。名前はまだ無い。」夏目漱石の長編小説で、明治38年（1905年）1月から雑誌『ホトトギス』に" +
        "連載された。英語の題は I Am a Cat。";

    [Bindable] private double _verticalLength = 300;

    [Bindable] private int _tiers = 1;

    [Bindable] private HorizontalTextAlignment _lineAlignment = HorizontalTextAlignment.Left;

    // Natsume Sōseki, "I Am a Cat": no line may start with a closing bracket, a small kana or a full stop.
    public string JapaneseSample =>
        "「吾輩は猫である。名前はまだ無い。」どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー泣いていた事だけは記憶している。";

    // The opening of the Gospel of John in the Synodal translation, its first letter a decorative initial.
    public string GospelSample =>
        "В начале было Слово, и Слово было у Бога, и Слово было Бог. Оно было в начале у Бога. Всё чрез Него начало " +
        "быть, и без Него ничто не начало быть, что начало быть. В Нем была жизнь, и жизнь была свет человеков.";

    // A table of contents, page numbers right-aligned behind dot leaders, and prices lined up on the decimal point.
    public string ContentsSample => "Introduction\t1\nThe paragraph composer\t17\nHyphenation by patterns\t123";

    public string PricesSample => "Tea\t3.50\nCoffee\t12.75\nA whole cake\t120.00";

    public FontFamily CurvesFont { get; } = FromFile(SandboxContentAssets.Fonts.OTFFonts.Poppins_Medium);

    public string TrimmedSample => "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful.";

    public string AlignedTabsSample => "Name\tValue\nSize\t12 px\nColor\tdeep blue";

    public string LongWordsSample =>
        "Достопримечательности высокопроизводительного сельскохозяйственного производства неизменно привлекают " +
        "внимание любознательных путешественников.";

    public string DatesSample => "今日は2026年10月17日で、3つのりんごと12個のみかんを買いました。";

    [Bindable] private double _columnWidth = 220;

    [Bindable, Affects(nameof(ColumnAlignment))] private bool _justified;

    public HorizontalTextAlignment ColumnAlignment => Justified ? HorizontalTextAlignment.Justify : HorizontalTextAlignment.Left;

    // The opening of the Brothers Grimm's "The Frog King", set both ways side by side.
    public string ProseSample =>
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but the " +
        "youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone in her " +
        "face. Close by the king's castle lay a great dark forest, and under an old lime tree in the forest was a well, " +
        "and when the day was very warm, the king's child went out into the forest and sat down by the side of the cool " +
        "fountain.";

    public string RussianSample =>
        "Достопримечательности высокопроизводительного сельскохозяйственного производства неизменно привлекают внимание любознательных путешественников.";

    public string EnglishSample =>
        "Internationalization of extraordinarily sophisticated typographical applications necessitates comprehensive hyphenation dictionaries.";

    public string GermanSample =>
        "Die Donaudampfschifffahrtsgesellschaft veröffentlichte ihre Rechtschreibempfehlungen für Lebensversicherungsunternehmen.";

    public string FrenchSample =>
        "L'internationalisation des applications typographiques extraordinairement sophistiquées nécessite des dictionnaires.";

    public string SpanishSample =>
        "La internacionalización de las aplicaciones tipográficas extraordinariamente sofisticadas necesita diccionarios.";

    [Bindable, Affects(nameof(WordSpacing))] private double _wordMinimum = 80;

    [Bindable, Affects(nameof(WordSpacing))] private double _wordMaximum = 133;

    [Bindable, Affects(nameof(LetterSpacing))] private double _letterMinimum;

    [Bindable, Affects(nameof(LetterSpacing))] private double _letterMaximum;

    [Bindable] private double _tracking;

    [Bindable] private HorizontalTextAlignment _lastLineAlignment = HorizontalTextAlignment.Left;

    [Bindable] private HorizontalTextAlignment _singleWordJustification = HorizontalTextAlignment.Left;

    [Bindable, Affects(nameof(GlyphScaling))] private double _glyphMinimum = 100;

    [Bindable, Affects(nameof(GlyphScaling))] private double _glyphMaximum = 100;

    public SpacingRange GlyphScaling => Range(GlyphMinimum, 100, GlyphMaximum);

    // InDesign's justification settings of the paragraph topic, in percents of the space; the desired value kept between
    // the two a range slider sets.
    public SpacingRange WordSpacing => Range(WordMinimum, 100, WordMaximum);

    public SpacingRange LetterSpacing => Range(LetterMinimum, 0, LetterMaximum);

    private static SpacingRange Range(double minimum, double desired, double maximum) =>
        new(minimum / 100, Math.Clamp(desired, minimum, Math.Max(minimum, maximum)) / 100, Math.Max(minimum, maximum) / 100);

    // A justified line holding one word, then the paragraph's last line.
    public string SingleWordSample => "Incomprehensibilities notwithstanding";

    // The Universal Declaration of Human Rights, article 1: justified with kashidas.
    public string ArabicParagraphSample =>
        "يولد جميع الناس أحرارًا متساوين في الكرامة والحقوق. وقد وهبوا عقلاً وضميرًا وعليهم أن يعامل بعضهم بعضًا " +
        "بروح الإخاء.";

    [Bindable] private bool _kashidas = true;

    // How far the run of the basics topic's baseline shift sample is raised; negative lowers it.
    [Bindable] private double _runShift = 6;

    // The line height, line spacing and most lines of the basics topic's lines sample; 0 lines for any number.
    [Bindable] private double _sampleLineHeight = 30;

    [Bindable] private double _sampleLineSpacing;

    [Bindable] private int _sampleMaxLines = 3;

    // How many times the basics topic's counting link was activated.
    [Bindable, Affects(nameof(LinkClicksText))] private int _linkClicks;

    public string LinkClicksText => $"({LinkClicks})";

    [Command] private void CountLink() => LinkClicks++;

    // Genesis 1:1-2, unpointed.
    public string HebrewSample =>
        "בראשית ברא אלהים את השמים ואת הארץ. והארץ היתה תהו ובהו וחשך על פני תהום ורוח אלהים מרחפת על פני המים.";

    // A word with soft hyphens written into it breaks only at them, whatever the patterns say.
    public string SoftHyphenSample =>
        "Supercali­fragilistic­expiali­docious, even though the sound of it is something quite atro­cious.";

    [Bindable] private string _hyphenationEditable =
        "Высокопроизводительные текстовые редакторы переносят слова автоматически.";

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
        var samples = FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures, 12);
        return font.FeatureCatalog.GSUBFeatures
            .Where(feature => samples.ContainsKey(feature.Info.Tag))
            .Select(feature => new FontFeatureItem(feature.Info.Tag, feature.Name, feature.ValueCount,
                samples[feature.Info.Tag]))
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

    private static FontFamily FromFile(string path)
    {
        return new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, path)));
    }

    // The new typesetting stand: CJK punctuation as CSS's text-spacing-trim and hanging-punctuation set it.
    [Bindable] private TextSpacingTrim _spacingTrim = TextSpacingTrim.Normal;

    [Bindable] private HangingPunctuation _hanging = HangingPunctuation.AllowEnd;

    public string PunctuationSample =>
        "「吾輩は猫である。」（名前はまだ無い。）どこで生れたか、とんと見当がつかぬ。「何でも薄暗い所で」泣いていた。";

    // Words broken inside a ligature, and an Arabic word at its soft hyphen: each half set again by itself.
    public string LigatureSample => "officially affluent offices efficiently shuffled";

    public string ArabicHyphenSample => "مكت­بات المكت­بات والمكت­بات";

    // Our own font of 'SVG' color glyphs: classes, the cascade, a clip in the box's units, an ancestor's clip, a wrong
    // value, a clip by a use.
    public FontFamily SvgStylesFont { get; } = FromFile(SandboxContentAssets.Fonts.AdamantiumSvgStyles);

    // Our own font of 600 color images ('sbix'), each its own color and stripes, in rows of twenty: scrolled through, the
    // atlas lets the rows no longer shown go when the images take more than the budget.
    public FontFamily TilesFont { get; } = FromFile(SandboxContentAssets.Fonts.AdamantiumColorTiles);

    public IReadOnlyList<string> TileRows { get; } = Enumerable.Range(0, 30)
        .Select(row => string.Concat(Enumerable.Range(0, 20).Select(column => (char)(0xE000 + row * 20 + column))))
        .ToArray();

    // A layer of the color atlas: 1024 by 1024 pixels of four bytes with their smaller levels, holding 32 of the tiles.
    private const double LayerMegabytes = 1024 * 1024 * 4 * 4 / 3.0 / 1_000_000;

    private const int TilesInALayer = 32;

    // The budget of the color atlas while the stand is shown; the default when it is left.
    [Bindable, Affects(nameof(BudgetImages), nameof(BudgetMegabytes))] private int _colorBudget = 8;

    public string BudgetImages => (ColorBudget * TilesInALayer).ToString();

    public string BudgetMegabytes => (ColorBudget * LayerMegabytes).ToString("0");

    // What the atlas holds now, read four times a second while the stand is shown.
    [Bindable] private string _colorImages = "0";

    [Bindable] private string _colorLayers = "0";

    [Bindable] private string _colorMegabytes = "0";

    // All 600 tiles at once: what the atlas kept before it let images go.
    public string AllTilesLayers => Math.Ceiling(600.0 / TilesInALayer).ToString();

    public string AllTilesMegabytes => (Math.Ceiling(600.0 / TilesInALayer) * LayerMegabytes).ToString("0");

    private void ReadTheAtlas()
    {
        var layers = FontAtlasStore.ColorImageLayers;
        ColorImages = FontAtlasStore.ColorImages.ToString();
        ColorLayers = layers.ToString();
        ColorMegabytes = (layers * LayerMegabytes).ToString("0");
    }

    private Adamantium.UI.Core.Dispatcher.DispatcherTimer _atlasWatch;

    partial void OnColorBudgetChanged(int value)
    {
        if (Stand == TextStand.Typesetting)
        {
            FontAtlasStore.ColorImageLayerBudget = value;
        }
    }

    // On the typesetting stand the budget is the stand's and the layers taken are shown; elsewhere the default holds.
    private void WatchTheAtlas(bool shown)
    {
        FontAtlasStore.ColorImageLayerBudget = shown ? ColorBudget : DefaultColorBudget;
        if (shown && _atlasWatch == null)
        {
            _atlasWatch = new Adamantium.UI.Core.Dispatcher.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.25) };
            _atlasWatch.Tick += (_, _) => ReadTheAtlas();
            _atlasWatch.Start();
        }
        else if (!shown && _atlasWatch != null)
        {
            _atlasWatch.Dispose();
            _atlasWatch = null;
        }
    }

    private static readonly int DefaultColorBudget = FontAtlasStore.ColorImageLayerBudget;
}
