using System;
using System.IO;
using Adamantium.MVVM;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Text tab: a message shown by TextBlocks (font size slider-driven), an editable TextBox two-way bound to the
/// same <see cref="Message"/>, and a TextBlock built from bindable <c>Run</c>s - the accent run's Text binds to Message
/// (so it live-updates while you type) and its Foreground binds to <see cref="AccentBrush"/>.</summary>
[ViewModel]
public partial class TextViewModel : TabPageViewModel
{
    public TextViewModel() : base("Text") { }

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

    public int MessageLength => Message?.Length ?? 0;

    private static FontFamily FromFile(string name)
    {
        return new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts", name)));
    }
}
