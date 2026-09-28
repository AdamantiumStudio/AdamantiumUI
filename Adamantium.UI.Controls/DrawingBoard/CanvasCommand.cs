using System;
using Adamantium.Core.Commands;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One canvas action as a command a button binds to; it reports its own availability, so a bound button disables
/// itself.</summary>
public sealed class CanvasCommand : ICommand
{
    private readonly Func<object, bool> _can;
    private readonly Action<object> _does;

    internal CanvasCommand(Action<object> does, Func<object, bool> can = null)
    {
        _does = does;
        _can = can;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter = null) => _can?.Invoke(parameter) ?? true;

    public void Execute(object parameter = null)
    {
        if (CanExecute(parameter)) _does(parameter);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
