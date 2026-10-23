using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>The strip along the bottom of a window or a document that tells how things stand - where the caret is, how
/// the file is saved, what the build found - as an IDE's has. Its fields are <see cref="StatusBarItem"/>s written in its
/// markup, or made for the data items of its <see cref="ItemsControl.ItemsSource"/>; a <see cref="Separator"/> stands
/// between groups, and any other element written in it - a progress bar - stands as it is. The fields line up from the
/// left, and one docked right with <c>DockPanel.Dock="Right"</c> from the right. It is no stop for the keyboard: a
/// field acts on a press.</summary>
public class StatusBar : ItemsControl
{
    static StatusBar()
    {
        FocusableProperty.OverrideMetadata(typeof(StatusBar), new PropertyMetadata(false));
    }

    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var field = new StatusBarItem();
        if (ItemContainerStyle != null)
        {
            field.Styles.Add(ItemContainerStyle);
        }

        return field;
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        base.PrepareContainer(container, item);
        if (container is not StatusBarItem field || ReferenceEquals(field, item))
        {
            return;
        }

        field.DataContext = item;
        field.ContentTemplate = ItemTemplate;
        field.ContentTemplateSelector = ItemTemplateSelector;
        field.Content = item;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusBarAutomationPeer(this);
}
