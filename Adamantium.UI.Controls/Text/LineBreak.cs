using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>Ends the line inside a <see cref="TextBlock"/>'s inlines: what follows starts on the next line of the same
/// paragraph (U+2028 LINE SEPARATOR), which keeps its direction. A justified line ending in it is not stretched.</summary>
[TrimSurroundingWhitespace]
public class LineBreak : Inline
{
}
