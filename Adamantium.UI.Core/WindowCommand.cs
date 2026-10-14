using Adamantium.Core.Commands;
using Adamantium.MVVM;

namespace Adamantium.UI.Core;

/// <summary>
/// A caption-bar command, the view model of one item in a window's LeftWindowCommands / RightWindowCommands. The bar
/// button shows the icon named by <see cref="IconKey"/>; <see cref="Label"/> is the text used when the command spills
/// into the overflow menu.
/// </summary>
public class WindowCommand : AdamantiumViewModel
{
    private string _iconKey;
    private object _content;
    private string _label;
    private ICommand _command;
    private object _commandParameter;
    private string _toolTip;
    private string _automationId;

    /// <summary>The key of the icon among the application's resources - a DrawingImage drawn in CurrentColor takes the
    /// caption's color. A key, not the picture: the view finds the resource.</summary>
    public string IconKey
    {
        get => _iconKey;
        set => SetProperty(ref _iconKey, value);
    }

    /// <summary>The overflow menu's text when <see cref="Label"/> is not set.</summary>
    public object Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value)) RaisePropertyChanged(nameof(OverflowLabel));
        }
    }

    /// <summary>Text shown for this command in the OVERFLOW menu (falls back to <see cref="Content"/> if unset).</summary>
    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value)) RaisePropertyChanged(nameof(OverflowLabel));
        }
    }

    /// <summary>Invoked when the command's bar button or overflow row is clicked.</summary>
    public ICommand Command
    {
        get => _command;
        set => SetProperty(ref _command, value);
    }

    public object CommandParameter
    {
        get => _commandParameter;
        set => SetProperty(ref _commandParameter, value);
    }

    /// <summary>Optional hover tooltip for the bar button.</summary>
    public string ToolTip
    {
        get => _toolTip;
        set => SetProperty(ref _toolTip, value);
    }

    /// <summary>The id automation finds the bar button by; the themes give it to the button, with <see cref="Label"/>
    /// as its name.</summary>
    public string AutomationId
    {
        get => _automationId;
        set => SetProperty(ref _automationId, value);
    }

    /// <summary>The overflow-menu label - <see cref="Label"/>, else the <see cref="Content"/> text.</summary>
    public object OverflowLabel => Label ?? Content;
}
