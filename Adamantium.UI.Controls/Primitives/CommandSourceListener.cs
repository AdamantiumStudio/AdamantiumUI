using System;
using Adamantium.Core.Commands;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Primitives;

// Weak, so a command owned by a long-lived view-model does not keep a removed source alive; a routed command is
// listened to only while the source is in a visual tree, where its route has somewhere to go.
internal sealed class CommandSourceListener<TSource> where TSource : class, IUIComponent
{
    private readonly TSource _source;
    private readonly Action<TSource> _askAgain;
    private ICommand _command;
    private WeakCanExecuteChangedRelay<TSource> _relay;

    public CommandSourceListener(TSource source, Action<TSource> askAgain)
    {
        _source = source;
        _askAgain = askAgain;
    }

    public void Listen(ICommand command)
    {
        _relay?.Detach();
        _relay = null;
        _command = command;
        if (command != null && (command is not RoutedCommand || _source.IsAttachedToVisualTree))
        {
            _relay = new WeakCanExecuteChangedRelay<TSource>(command, _source, _askAgain);
        }
    }

    public void Attached()
    {
        if (_command is RoutedCommand)
        {
            _relay ??= new WeakCanExecuteChangedRelay<TSource>(_command, _source, _askAgain);
            _askAgain(_source);
        }
    }

    public void Detached()
    {
        if (_command is RoutedCommand)
        {
            _relay?.Detach();
            _relay = null;
        }
    }
}
