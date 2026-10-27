using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Span"/> whose inlines are bold, unless they set a weight of their own or the character style
/// they take, set on it or inside it, sets one.</summary>
public class Bold : Span
{
    static Bold()
    {
        FontWeightProperty.OverrideMetadata(typeof(Bold),
            new PropertyMetadata((Adamantium.Core.Fonts.FontWeight?)Adamantium.Core.Fonts.FontWeight.Bold));
    }
}
