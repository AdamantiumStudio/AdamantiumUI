using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>Shared, UI-free glue between a dialog host and an <see cref="IDialogAware"/> view model: awaits
/// <see cref="IDialogAware.OnDialogOpenedAsync"/>, and on <see cref="IDialogAware.RequestClose"/> honors
/// <see cref="IDialogAware.CanCloseDialogAsync"/>, tears down the presentation, and completes <see cref="Completion"/> with
/// the result. Every <see cref="IDialogHost"/> reuses this so the lifecycle lives in one place.</summary>
public sealed class DialogSession
{
    private readonly TaskCompletionSource<IDialogResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDialogAware _aware;
    private readonly Action _dismiss;
    private bool _asking;

    private DialogSession(object dialogViewModel, Action dismiss)
    {
        _aware = dialogViewModel as IDialogAware;
        _dismiss = dismiss;
    }

    /// <summary>Begins a session for a dialog about to be presented, once its view model has opened. If it closed itself
    /// meanwhile, <see cref="Completion"/> is already complete and the host must not present it. <paramref name="dismiss"/>
    /// tears down the host's presentation and runs once, when the dialog actually closes.</summary>
    public static async Task<DialogSession> BeginAsync(object dialogViewModel, NavigationParameters parameters, Action dismiss,
        CancellationToken cancellationToken = default)
    {
        var session = new DialogSession(dialogViewModel, dismiss);
        if (session._aware != null)
        {
            session._aware.RequestClose += session.OnRequestClose;
            await session._aware.OnDialogOpenedAsync(parameters ?? new NavigationParameters(), cancellationToken);
        }
        return session;
    }

    public Task<IDialogResult> Completion => _completion.Task;

    /// <summary>Closes the dialog from the host side (e.g. Esc) as if the view model requested it, still respecting
    /// <see cref="IDialogAware.CanCloseDialogAsync"/>.</summary>
    public void RequestClose(IDialogResult result) => OnRequestClose(result);

    /// <summary>The presentation is already gone (e.g. the user closed the dialog's window): completes with
    /// <paramref name="result"/> without asking the view model.</summary>
    public void Close(IDialogResult result) => Complete(result);

    private async void OnRequestClose(IDialogResult result)
    {
        if (_completion.Task.IsCompleted || _asking)
        {
            return;
        }

        _asking = true;
        try
        {
            if (_aware == null || await _aware.CanCloseDialogAsync())
            {
                Complete(result);
            }
        }
        catch (Exception ex)
        {
            if (_completion.TrySetException(ex))
            {
                Dismiss();
            }
        }
        finally
        {
            _asking = false;
        }
    }

    private void Complete(IDialogResult result)
    {
        if (_completion.TrySetResult(result ?? new DialogResult(DialogButtonResult.None)))
        {
            Dismiss();
        }
    }

    private void Dismiss()
    {
        if (_aware != null)
        {
            _aware.RequestClose -= OnRequestClose;
        }
        _dismiss?.Invoke();
    }
}
