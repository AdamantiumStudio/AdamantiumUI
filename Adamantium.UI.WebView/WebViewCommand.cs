using Adamantium.Core.Commands;

namespace Adamantium.UI.WebView;

/// <summary>One of a <see cref="WebView"/>'s actions - back, forward, reload, stop - as a command a button binds to; it
/// reports its own availability, so a bound button disables itself.</summary>
public sealed class WebViewCommand : ICommand
{
    private readonly Action does;
    private readonly Func<bool> can;

    internal WebViewCommand(Action does, Func<bool> can)
    {
        this.does = does;
        this.can = can;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter = null) => can();

    public void Execute(object parameter = null)
    {
        if (CanExecute(parameter))
        {
            does();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
