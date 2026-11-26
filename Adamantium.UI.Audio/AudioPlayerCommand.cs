using Adamantium.Core.Commands;

namespace Adamantium.UI.Audio;

/// <summary>One of an <see cref="AudioPlayer"/>'s actions - play, stop, next, repeat - as a command a button binds to;
/// it reports its own availability, so a bound button disables itself.</summary>
public sealed class AudioPlayerCommand : ICommand
{
    private readonly Action _does;
    private readonly Func<bool> _can;

    internal AudioPlayerCommand(Action does, Func<bool> can)
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
