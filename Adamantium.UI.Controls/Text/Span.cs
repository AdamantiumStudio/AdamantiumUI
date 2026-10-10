using System;
using System.Collections.Specialized;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A group of inlines inside a <see cref="TextBlock"/>: the formatting it sets goes to every inline in it
/// that does not set its own, spans nest, and their lines add up. Its inlines are its logical children, so they inherit
/// its DataContext and its <c>Typography</c>.</summary>
public class Span : Inline
{
    private InlineCollection _inlines;

    /// <summary>The inlines in this span, in order.</summary>
    [Content]
    public InlineCollection Inlines
    {
        get
        {
            if (_inlines == null)
            {
                _inlines = [];
                _inlines.CollectionChanged += OnInlinesChanged;
            }

            return _inlines;
        }
    }

    private void OnInlinesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (Inline old in e.OldItems)
            {
                old.Changed -= OnInlineChanged;
                RemoveLogicalChild(old);
            }
        }

        if (e.NewItems != null)
        {
            foreach (Inline added in e.NewItems)
            {
                for (IFundamentalUIComponent at = this; at != null; at = at.LogicalParent)
                {
                    if (ReferenceEquals(at, added))
                    {
                        throw new InvalidOperationException("A span cannot hold itself or a span it is in.");
                    }
                }

                AddLogicalChild(added);
                added.Changed += OnInlineChanged;
            }
        }

        RaiseChanged();
    }

    private void OnInlineChanged(object sender, EventArgs e) => RaiseChanged();
}
