using System;
using Adamantium.Core.Commands;

namespace Adamantium.UI.Controls.Printing;

/// <summary>One action of the print dialog as a command a button binds to; it reports whether it can be done now, so a
/// bound button disables itself.</summary>
public sealed class PrintDialogCommand : ICommand
{
    private readonly Func<bool> _can;
    private readonly Action _does;

    internal PrintDialogCommand(Action does, Func<bool> can = null)
    {
        _does = does;
        _can = can;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter = null) => _can?.Invoke() ?? true;

    public void Execute(object parameter = null)
    {
        if (CanExecute(parameter))
        {
            _does();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
