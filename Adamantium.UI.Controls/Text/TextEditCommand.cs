using System;
using Adamantium.Core.Commands;

namespace Adamantium.UI.Controls.Text;

/// <summary>One editing action of a text box as a command a menu row binds to; it reports whether it applies now, so a
/// bound row disables itself.</summary>
public sealed class TextEditCommand : ICommand
{
    private readonly Func<bool> _can;
    private readonly Action _does;

    internal TextEditCommand(Action does, Func<bool> can)
    {
        _does = does;
        _can = can;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter = null) => _can();

    public void Execute(object parameter = null)
    {
        if (CanExecute(parameter))
        {
            _does();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
