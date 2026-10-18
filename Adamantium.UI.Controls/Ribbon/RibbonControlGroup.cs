using System.Collections.Specialized;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>A row of ribbon commands side by side, as Word's Bold, Italic and Underline, or its font and size boxes: it
/// stands in its group as one row of the group's columns, and the commands in it are drawn small - their icons
/// alone.</summary>
public class RibbonControlGroup : StackPanel
{
    public RibbonControlGroup()
    {
        Orientation = Orientation.Horizontal;
        Ribbon.SetMaxSize(this, RibbonSize.Medium);
        Children.CollectionChanged += OnCommandsChanged;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupAutomationPeer(this);

    private static void OnCommandsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
        {
            return;
        }

        foreach (var command in e.NewItems)
        {
            if (command is IAdamantiumComponent component)
            {
                Ribbon.SetSize(component, RibbonSize.Small);
            }
        }
    }
}
