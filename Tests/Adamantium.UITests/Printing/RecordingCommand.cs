using System;
using Adamantium.Core.Commands;

namespace Adamantium.UITests.Printing;

/// <summary>A command that remembers what it was last run with.</summary>
internal sealed class RecordingCommand : ICommand
{
    public object Parameter { get; private set; }

    public event EventHandler CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object parameter = null) => true;

    public void Execute(object parameter = null) => Parameter = parameter;

    public void RaiseCanExecuteChanged()
    {
    }
}
