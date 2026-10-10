using System.Collections.ObjectModel;

namespace Adamantium.UI.Controls.Text;

/// <summary>The ordered inline content of a <see cref="TextBlock"/> or a <see cref="Span"/> (their <c>Inlines</c>). A
/// typed <see cref="ObservableCollection{T}"/> so the owner re-lays-out when inlines are added or removed; clearing it
/// reports each removed inline, so its owner lets go of every one.</summary>
public class InlineCollection : ObservableCollection<Inline>
{
    protected override void ClearItems()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }
}
