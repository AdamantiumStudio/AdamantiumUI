using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Span"/> whose inlines are bold, unless they set a weight of their own.</summary>
public class Bold : Span
{
    static Bold()
    {
        FontWeightProperty.OverrideMetadata(typeof(Bold),
            new PropertyMetadata((Adamantium.Fonts.FontWeight?)Adamantium.Fonts.FontWeight.Bold));
    }
}
