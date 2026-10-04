using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>Lifecycle a dialog view model opts into: it is told when it opens, can veto closing, and closes itself (with
/// a result) by raising <see cref="RequestClose"/>.</summary>
public interface IDialogAware
{
    /// <summary>Shown on the dialog's title bar (draggable overlay chrome). May be empty.</summary>
    string Title { get; }

    /// <summary>Awaited before the dialog is shown. A dialog that raises <see cref="RequestClose"/> meanwhile is never
    /// shown.</summary>
    Task OnDialogOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Asked before the dialog closes, by its own <see cref="RequestClose"/> or by the user; false keeps it open.
    /// The title-bar close of a dialog in its own window is final and is not asked.</summary>
    Task<bool> CanCloseDialogAsync();

    event Action<IDialogResult> RequestClose;
}
