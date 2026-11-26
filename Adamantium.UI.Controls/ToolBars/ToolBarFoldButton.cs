using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// What a command of a bar down the side stands as when it is too wide for the column (<see cref="ToolBarItemForm.Folded"/>):
/// a toggle with the command's icon, or the first letters of its label, that opens the command itself on its
/// <see cref="ToolBarItem"/>'s card beside the column. Made by that item; drawn as the theme draws a toggle.
/// </summary>
public class ToolBarFoldButton : ToggleButton
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(Header), typeof(object), typeof(ToolBarFoldButton), new PropertyMetadata(null, OnHeaderChanged));

    /// <summary>The command's label - the text its <c>LabeledBy</c> names, else its name - shown as the button's tooltip
    /// and over the command on the card.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        internal set => SetReadOnlyValue(HeaderProperty, value);
    }

    // Named by it to automation as well - not by the two letters it shows.
    private static void OnHeaderChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        a.SetCurrentValue(ToolTipService.ToolTipProperty, e.NewValue);
        a.SetCurrentValue(AutomationProperties.NameProperty, e.NewValue as string);
    }
}
