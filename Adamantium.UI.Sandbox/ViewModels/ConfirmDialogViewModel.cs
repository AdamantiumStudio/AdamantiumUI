using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A confirm dialog view model (IDialogAware): shows a title/message and closes itself with Ok/Cancel via
/// RequestClose. Shown by NavigationDemoViewModel through IDialogService on the overlay host.</summary>
[ViewModel]
public partial class ConfirmDialogViewModel : AdamantiumViewModel, IDialogAware
{
    [Bindable] private string title = DialogStrings.Confirm;
    [Bindable] private string message = DialogStrings.AreYouSure;

    public Task OnDialogOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        if (parameters != null)
        {
            if (parameters.TryGetValue<string>("title", out var t))
            {
                Title = t;
            }
            if (parameters.TryGetValue<string>("message", out var m))
            {
                Message = m;
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> CanCloseDialogAsync() => Task.FromResult(true);

    public event Action<IDialogResult> RequestClose;

    [Command] private void Ok() => RequestClose?.Invoke(DialogResult.Ok());

    [Command] private void Cancel() => RequestClose?.Invoke(DialogResult.Cancel());
}
