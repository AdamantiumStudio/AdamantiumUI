using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Sandbox.Views;

/// <summary>Picks this shell's quick-access templates from its own command items, e.g. by whether a command has a checked
/// state.</summary>
public class ShellQuickAccessTemplateSelector : RibbonQuickAccessTemplateSelector
{
    /// <summary>A command that is ON or OFF. Its template binds the ITEM's state, so the button in the caption and the
    /// one in the ribbon are two views of one value rather than two values kept in step.</summary>
    public DataTemplate Toggle { get; set; }

    /// <summary>Only the case the base does not know about. Everything else - a command's own compact form, the ordinary
    /// icon button the theme draws - stays the bar's answer.</summary>
    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container)
    {
        if (item is QuickAccessCommand command && command.IsChecked.HasValue) return Toggle;

        return base.SelectTemplate(item, container);
    }
}
