using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>The effects of a text block or an inline (its <c>TextEffects</c>); observable, so the text is drawn again when
/// one is added, removed or changed. Each effect in it is a logical child of the holder, so it binds against the holder's
/// data. It holds no null.</summary>
public sealed class TextEffectCollection : ObservableCollection<TextEffect>
{
    private readonly FundamentalUIComponent _holder;

    internal TextEffectCollection(FundamentalUIComponent holder)
    {
        _holder = holder;
    }

    internal event EventHandler Changed;

    protected override void InsertItem(int index, TextEffect item)
    {
        base.InsertItem(index, item ?? throw new ArgumentNullException(nameof(item)));
        Hold(item);
    }

    protected override void SetItem(int index, TextEffect item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Release(this[index]);
        base.SetItem(index, item);
        Hold(item);
    }

    protected override void RemoveItem(int index)
    {
        Release(this[index]);
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Hold(TextEffect effect)
    {
        _holder.AddLogicalChild(effect);
        effect.Changed += OnEffectChanged;
        effect.ReparentTransform();
        effect.SetInBlock(_holder is TextBlock || _holder is Inline { InBlock: true });
    }

    private void Release(TextEffect effect)
    {
        effect.Changed -= OnEffectChanged;
        _holder.RemoveLogicalChild(effect);
        effect.ReparentTransform();
        effect.SetInBlock(effect.LogicalParent is TextBlock || effect.LogicalParent is Inline { InBlock: true });
    }

    private void OnEffectChanged(object sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
