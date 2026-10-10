using System;
using System.Collections.ObjectModel;

namespace Adamantium.UI.Controls.Text;

/// <summary>The lines of an inline (its <c>DecorationLines</c>); observable, so the text block lays its text out again
/// when one is added, removed or changed. It holds no null.</summary>
public sealed class TextDecorationCollection : ObservableCollection<TextDecoration>
{
    protected override void InsertItem(int index, TextDecoration item) =>
        base.InsertItem(index, item ?? throw new ArgumentNullException(nameof(item)));

    protected override void SetItem(int index, TextDecoration item) =>
        base.SetItem(index, item ?? throw new ArgumentNullException(nameof(item)));

    protected override void ClearItems()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }
}
