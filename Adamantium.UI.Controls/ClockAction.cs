using System;
using Adamantium.Core.Commands;

namespace Adamantium.UI.Controls;

internal sealed class ClockAction(Action does, Func<bool> can) : ICommand
{
    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter = null) => can();

    public void Execute(object parameter = null)
    {
        if (can())
        {
            does();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
