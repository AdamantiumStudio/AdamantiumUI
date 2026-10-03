using System.Collections;
using Adamantium.Core.Commands;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>A request to add a command to the quick-access bar or remove it, describing the command so the application
/// builds its own item. Also the parameter of <see cref="Ribbon.AddToQuickAccessCommandProperty"/>.</summary>
public class RibbonQuickAccessEventArgs : RoutedEventArgs
{
    public RibbonQuickAccessEventArgs(RoutedEvent routedEvent, IUIComponent command)
    {
        RoutedEvent = routedEvent;
        OriginalSource = command;
        Source = command;
        Command = command;

        Item = (command as IFundamentalUIComponent)?.DataContext as IQuickAccessItem;
        Icon = Ribbon.GetIcon(command);
        Key = Ribbon.GetQuickAccessKey(command);
        Label = (command as ContentControl)?.Content?.ToString();
        Template = Ribbon.GetQuickAccessTemplate(command);
        ToolTip = command is AdamantiumComponent component ? ToolTipService.GetToolTip(component) : null;

        // Not everything in a group is a button - a slider, a drop-down and a label all sit in one, and asking them for
        // a Command they never declared is asking for a property that is not theirs.
        if (command is Primitives.ButtonBase button)
        {
            Action = button.Command;
            ActionParameter = button.CommandParameter;
        }

        // What a drop-down drops, taken as DATA. The menu itself is not handed over: a ContextMenu is a logical CHILD,
        // and a logical child has one parent - lending it to the bar would take it away from the ribbon.
        if (command is RibbonDropDownButton dropDown && dropDown.DropDownMenu is { } menu)
        {
            DropDownItems = menu.ItemsSource;
            DropDownItemTemplate = menu.ItemTemplate;
        }
    }

    /// <summary>The ribbon command that was asked about. Held so an application can read whatever else it needs off it -
    /// but it is NOT what should be stored: a control outlives nothing, and re-templating replaces it.</summary>
    public IUIComponent Command { get; }

    /// <summary>The item the command is drawn for, when the application builds its commands from data: its DataContext,
    /// if that is an <see cref="IQuickAccessItem"/>. This is the item to put in the bar or take out - nothing has to be
    /// rebuilt from the description below.</summary>
    public IQuickAccessItem Item { get; }

    /// <summary>What marks the command - the small icon it draws in the bar.</summary>
    public object Icon { get; }

    /// <summary>What the application calls this command (<see cref="Ribbon.QuickAccessKeyProperty"/>), or null for one
    /// it never named. This is the identity to answer by - the control above is not.</summary>
    public object Key { get; }

    /// <summary>The command's own COMPACT form (see <see cref="Ribbon.QuickAccessTemplateProperty"/>), or null to be
    /// drawn as an ordinary icon button. This is what a slider or a drop-down hands over instead of pretending to be a
    /// button - the bar builds a fresh visual from it, it is not the ribbon's control on loan.</summary>
    public DataTemplate Template { get; }

    /// <summary>What the command calls itself - its words, for a bar that shows them (an overflow row does). Handed over
    /// as text, so no one has to reach into the control to read its content.</summary>
    public string Label { get; }

    public object ToolTip { get; }

    /// <summary>What the command DOES, and what a button in the bar has to run. Null when what asked was not a button.</summary>
    public ICommand Action { get; }

    /// <summary>The rows of a drop-down command's menu, and how one row is drawn. Null for anything that drops nothing.
    /// A menu authored as literal <c>MenuItem</c> children cannot travel - state it as <c>ItemsSource</c> to let the
    /// command keep its arrow in the bar.</summary>
    public IEnumerable DropDownItems { get; }

    public DataTemplate DropDownItemTemplate { get; }

    public object ActionParameter { get; }
}
