using System.Collections;
using System.ComponentModel;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A command of this shell as data. The ribbon's button, the quick-access bar's button and the customize page's
/// row are all drawn from it, so its words, its icon and what it runs are stated once. A command that is ON or OFF keeps
/// that state here, and every button showing it is a view of this one value.</summary>
public class ShellCommand : WindowCommand, IQuickAccessItem, INotifyPropertyChanged
{
    private string _name;
    private bool? _isChecked;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Its key in the shell's phrase table - what every view of it says. The caption bar's overflow takes text,
    /// so it is said there too.</summary>
    public string Name
    {
        get => _name;
        init
        {
            _name = value;
            Label = Languages.Say(RibbonShellStrings.Current, value);
            ToolTip = Label;
        }
    }

    /// <summary>The tabs it stands on.</summary>
    public ShellTab Tabs { get; init; }

    /// <summary>The rows it drops, for a command that drops a menu; null for one that does not.</summary>
    public IEnumerable DropDownItems { get; init; }

    /// <summary>The rows of the shell's own right-click menu on it; null for the ribbon's.</summary>
    public IEnumerable MenuRows { get; init; }

    /// <summary>ON or OFF for a command with such a state, null for one without.</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public object Key => Name;

    public ICommand Action => Command;

    /// <summary>None of its own: the shell's bar picks how each kind of command is drawn.</summary>
    public DataTemplate QuickAccessTemplate => null;
}
