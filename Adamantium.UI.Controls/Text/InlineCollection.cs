using System;
using System.Collections.ObjectModel;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>The ordered inline content of a <see cref="TextBlock"/> or a <see cref="Span"/> (their <c>Inlines</c>). A
/// typed <see cref="ObservableCollection{T}"/> so the owner re-lays-out when inlines are added or removed; clearing it
/// reports each removed inline, so its owner lets go of every one. Text written among the inlines in markup becomes a
/// <see cref="Run"/>.</summary>
[ContentWrapper(WrapperType = typeof(Run))]
public class InlineCollection : ObservableCollection<Inline>
{
    // The owner's say on an inline before it is in: it throws for one it cannot hold.
    internal Action<Inline> Admit { get; set; }

    protected override void InsertItem(int index, Inline item)
    {
        Admit?.Invoke(item);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, Inline item)
    {
        Admit?.Invoke(item);
        base.SetItem(index, item);
    }

    protected override void ClearItems()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }
}
