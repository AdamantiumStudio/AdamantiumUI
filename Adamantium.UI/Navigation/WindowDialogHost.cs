using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;

namespace Adamantium.UI.Navigation;

/// <summary>Window dialog host: shows the dialog in its OWN window (a shell from the window registry) instead of an
/// in-window overlay. Loads the resolved view into the shell and closes it when the view model raises RequestClose;
/// the title-bar close also completes the dialog (as Cancel), without asking it. Lifecycle/result via <see cref="DialogSession"/>.
/// NOT yet modal - input to the owner window is not blocked; that wiring is a follow-up.</summary>
public sealed class WindowDialogHost : IDialogHost
{
    private readonly IUIApplication _application;
    private readonly IViewLocator _viewLocator;
    private readonly IWindowShellRegistry _shells;

    public WindowDialogHost(IUIApplication application, IViewLocator viewLocator, IWindowShellRegistry shells)
    {
        _application = application;
        _viewLocator = viewLocator;
        _shells = shells;
    }

    public DialogHostKind Kind => DialogHostKind.Window;

    public async Task<IDialogResult> ShowAsync(object dialogViewModel, NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        WindowBase shell = null;
        var closed = false;
        var session = await DialogSession.BeginAsync(dialogViewModel, parameters, () =>
        {
            if (shell == null || closed)
            {
                return;
            }
            closed = true;
            shell.Close();
        }, cancellationToken);
        if (session.Completion.IsCompleted)
        {
            return await session.Completion;
        }

        // Window + render-service creation must run on the UI thread (as in WindowNavigationBackend).
        await _application.ExecuteOnUIThreadAsync(() =>
        {
            var aware = dialogViewModel as IWindowAware;
            var dialog = dialogViewModel as IDialogAware;
            if (_shells.Create(aware?.WindowShellKey) is not WindowBase created)
            {
                return;
            }
            shell = created;
            shell.RemembersPlacement = false;

            shell.Title = !string.IsNullOrEmpty(aware?.Title) ? aware.Title : dialog?.Title ?? string.Empty;
            if (string.IsNullOrEmpty(aware?.Title) && dialog is INotifyPropertyChanged observed)
            {
                void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(IDialogAware.Title))
                    {
                        shell.Title = dialog.Title;
                    }
                }

                observed.PropertyChanged += OnPropertyChanged;
                shell.Closed += (_, _) => observed.PropertyChanged -= OnPropertyChanged;
            }
            shell.ClientWidth = aware is { Width: > 0 } ? aware.Width : 440;
            shell.ClientHeight = aware is { Height: > 0 } ? aware.Height : 260;

            // Show FIRST (it attaches the window to the application and builds its tree + render service + theme), THEN
            // load the content, so the view joins a live, themed window (same order as WindowNavigationBackend).
            shell.Show();
            shell.Content = _viewLocator.ResolveView(dialogViewModel);

            shell.Closed += (_, _) =>
            {
                closed = true;
                session.Close(DialogResult.Cancel());
            };

            shell.Show();
        });

        return shell == null ? new DialogResult(DialogButtonResult.None) : await session.Completion;
    }
}
