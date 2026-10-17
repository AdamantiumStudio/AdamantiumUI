using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Span"/> whose inlines are italic, unless they set a style of their own or the character style
/// they take, set on it or inside it, sets one.</summary>
public class Italic : Span
{
    static Italic()
    {
        FontStyleProperty.OverrideMetadata(typeof(Italic),
            new PropertyMetadata((Adamantium.Fonts.FontStyle?)Adamantium.Fonts.FontStyle.Italic));
    }
}
