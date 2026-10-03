using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Sandbox.ViewModels;

namespace Adamantium.UI.Sandbox.Views;

/// <summary>Picks how this shell's bar draws each of its commands, by what the command is. A plain command keeps the
/// icon button the theme draws.</summary>
public class ShellQuickAccessTemplateSelector : RibbonQuickAccessTemplateSelector
{
    /// <summary>A command that is ON or OFF. Its template binds the command's own state, so the button in the caption and
    /// the one in the ribbon are two views of one value.</summary>
    public DataTemplate Toggle { get; set; }

    /// <summary>A command that drops a menu and runs nothing itself.</summary>
    public DataTemplate DropDown { get; set; }

    /// <summary>A command that runs, with a menu of its other ways beside it.</summary>
    public DataTemplate Split { get; set; }

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container)
    {
        if (item is not ShellCommand command)
        {
            return base.SelectTemplate(item, container);
        }

        if (command.IsChecked.HasValue)
        {
            return Toggle;
        }

        if (command.DropDownItems != null)
        {
            return command.Command != null ? Split : DropDown;
        }

        return base.SelectTemplate(item, container);
    }
}
