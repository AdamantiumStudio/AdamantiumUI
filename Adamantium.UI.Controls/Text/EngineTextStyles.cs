using System.Collections.Generic;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

// The engine's styles for the text styles a text lays out by: one per style for a layout, the ones they are based on
// mapped with them, every value as the text's scope answers it; their brushes held by the text.
internal sealed class EngineTextStyles(IUIComponent scope, HeldBrushes brushes)
{
    private readonly Dictionary<TextFormat, object> _styles = new();

    public void Clear() => _styles.Clear();

    public Adamantium.Graphics.Fonts.ParagraphStyle Of(ParagraphStyle style)
    {
        if (_styles.TryGetValue(style, out var known))
        {
            return (Adamantium.Graphics.Fonts.ParagraphStyle)known;
        }

        Hold(style);
        var engine = new Adamantium.Graphics.Fonts.ParagraphStyle(style.Name, style.OwnParagraphAttributes(scope),
            style.OwnTextAttributes(scope), style.BasedOn is { } based && !based.Reaches(style) ? Of(based) : null);
        _styles[style] = engine;
        return engine;
    }

    public Adamantium.Graphics.Fonts.CharacterStyle Of(CharacterStyle style)
    {
        if (_styles.TryGetValue(style, out var known))
        {
            return (Adamantium.Graphics.Fonts.CharacterStyle)known;
        }

        Hold(style);
        var engine = new Adamantium.Graphics.Fonts.CharacterStyle(style.Name, style.OwnTextAttributes(scope),
            style.BasedOn is { } based && !based.Reaches(style) ? Of(based) : null);
        _styles[style] = engine;
        return engine;
    }

    private void Hold(TextFormat style)
    {
        brushes.Add(style.Scoped<Brush>(TextFormat.ForegroundProperty, scope));
        brushes.Add(style.Scoped<Brush>(TextFormat.BackgroundProperty, scope));
    }

    // A value a text element sets itself, not one a style or a parent gives it: that one wins over its text styles.
    public static bool SetsItself(AdamantiumComponent text, AdamantiumProperty property) =>
        text.GetValueSource(property) is ValuePriority.Animation or ValuePriority.Local or ValuePriority.Binding
            or ValuePriority.Template;

    // The face of a text element's text in its own styles: what it sets itself, else its character style, else its
    // paragraph style, else what it takes.
    public static (FontWeight Weight, FontStyle Style, FontStretch Stretch) Face(UIComponent text,
        CharacterStyle characterStyle, ParagraphStyle paragraphStyle) =>
    (
        SetsItself(text, UIComponent.FontWeightProperty)
            ? text.FontWeight
            : characterStyle?.ResolvedWeight(text) ?? paragraphStyle?.ResolvedWeight(text) ?? text.FontWeight,
        SetsItself(text, UIComponent.FontStyleProperty)
            ? text.FontStyle
            : characterStyle?.ResolvedStyle(text) ?? paragraphStyle?.ResolvedStyle(text) ?? text.FontStyle,
        SetsItself(text, UIComponent.FontStretchProperty)
            ? text.FontStretch
            : characterStyle?.ResolvedStretch(text) ?? paragraphStyle?.ResolvedStretch(text) ?? text.FontStretch
    );

    // A text element's whole text in its own styles, what it sets itself over them.
    public void Style(AttributedText attributed, UIComponent text, CharacterStyle characterStyle,
        ParagraphStyle paragraphStyle)
    {
        var length = attributed.Text.Length;
        if (length == 0)
        {
            return;
        }

        attributed.Apply(0, length, new TextAttributes
        {
            FontSize = SetsItself(text, UIComponent.FontSizeProperty) ? text.FontSize : null,
            Foreground = SetsItself(text, UIComponent.ForegroundProperty) ? (text.Foreground as SolidColorBrush)?.Color : null,
        });
        if (characterStyle != null)
        {
            attributed.SetCharacterStyle(0, length, Of(characterStyle));
        }

        if (paragraphStyle != null)
        {
            attributed.SetParagraphStyle(0, length, Of(paragraphStyle));
        }
    }
}
