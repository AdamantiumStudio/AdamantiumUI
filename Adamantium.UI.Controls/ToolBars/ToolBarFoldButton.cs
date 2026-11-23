using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// What a command of a bar down the side stands as when it is too wide for the column (<see cref="ToolBarItemForm.Folded"/>):
/// a toggle with the command's icon, or the first letters of its label, that opens the command itself on its
/// <see cref="ToolBarItem"/>'s card beside the column. Made by that item; drawn as the theme draws a toggle.
/// </summary>
public class ToolBarFoldButton : ToggleButton
{
    private static readonly AdamantiumPropertyKey HeaderPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(Header), typeof(object), typeof(ToolBarFoldButton), new PropertyMetadata(null, OnHeaderChanged));

    public static readonly AdamantiumProperty HeaderProperty = HeaderPropertyKey.Property;

    /// <summary>The command's label - the text its <c>LabeledBy</c> names, else its name - shown as the button's tooltip
    /// and over the command on the card.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        internal set => SetValue(HeaderPropertyKey, value);
    }

    private static void OnHeaderChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        a.SetCurrentValue(ToolTipService.ToolTipProperty, e.NewValue);
}
