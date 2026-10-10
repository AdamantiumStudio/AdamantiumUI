using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>A control set into a <see cref="TextBlock"/>'s line, as WPF's: it takes the room its size asks for, stands
/// on the baseline, raises a line it is taller than and wraps as a word does. It takes input, focus and its DataContext
/// as any control. Horizontal text only.</summary>
public class InlineUIContainer : Inline
{
    public static readonly AdamantiumProperty ChildProperty = AdamantiumProperty.Register(nameof(Child),
        typeof(IMeasurableComponent), typeof(InlineUIContainer),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, ChildChanged));

    /// <summary>The control in the line.</summary>
    [Content]
    public IMeasurableComponent Child
    {
        get => GetValue<IMeasurableComponent>(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    private static void ChildChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var container = (InlineUIContainer)component;
        if (e.OldValue is IMeasurableComponent old)
        {
            container.RemoveLogicalChild(old);
        }

        if (e.NewValue is IMeasurableComponent added)
        {
            if (added.LogicalParent is InlineUIContainer other && !ReferenceEquals(other, container))
            {
                other.Child = null;
            }

            container.AddLogicalChild(added);
        }
    }
}
