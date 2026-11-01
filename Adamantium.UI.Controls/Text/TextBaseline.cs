using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>Where an element's text stands: the baseline of the first line of text it shows - a button's label - which
/// is what stands on a line's baseline when the element is set into text.</summary>
public static class TextBaseline
{
    /// <summary>How far below the top of <paramref name="element"/>, as last arranged, the baseline of the first line of
    /// horizontal text it shows lies; null when it shows none, or is not visible.</summary>
    public static double? Of(IUIComponent element)
    {
        if (element.Visibility != Visibility.Visible)
        {
            return null;
        }

        if (element is TextBlock { Layout: { LineCount: > 0 } layout, WritingMode: WritingMode.Horizontal } text
            && !string.IsNullOrEmpty(text.ShownText))
        {
            return layout.GetLine(0).Baseline;
        }

        foreach (var child in element.VisualChildren)
        {
            if (Of(child) is { } baseline)
            {
                return child.Bounds.Y + baseline;
            }
        }

        return null;
    }
}
