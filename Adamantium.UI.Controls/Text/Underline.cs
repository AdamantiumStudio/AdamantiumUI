using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Span"/> whose inlines are underlined, on top of any lines they draw themselves.</summary>
public class Underline : Span
{
    static Underline()
    {
        TextDecorationsProperty.OverrideMetadata(typeof(Underline),
            new PropertyMetadata(Adamantium.Graphics.Fonts.TextDecorations.Underline));
    }
}
