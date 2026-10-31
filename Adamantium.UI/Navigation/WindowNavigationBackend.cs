using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;

namespace Adamantium.UI.Navigation;

/// <summary>Bridges the UI-free navigation engine to real windows. Creates the window SHELL from the registry (a custom
/// window with your styles/chrome, or the default), loads the content view-model's View into it, and shows it - so a view
/// model can open a window without ever touching a UI type.</summary>
public sealed class WindowNavigationBackend : IWindowNavigationBackend
{
    private readonly IUIApplication _application;
    private readonly IViewLocator _viewLocator;
    private readonly IWindowShellRegistry _shells;
    private readonly Dictionary<object, IWindow> _windows = new();

    public WindowNavigationBackend(IUIApplication application, IViewLocator viewLocator, IWindowShellRegistry shells)
    {
        _application = application;
        _viewLocator = viewLocator;
        _shells = shells;
    }

    /// <summary>Opens the window and returns once it is on screen with its content drawn, or hidden or minimized before it
    /// was, so the window it replaces can go then, not leave nothing in its place meanwhile. A window closed before it
    /// came on screen fails the opening.</summary>
    public async Task<object> OpenWindowAsync(object contentViewModel, NavigationParameters parameters, string windowShell, CancellationToken cancellationToken)
    {
        IWindow created = null;
        WindowArrival arrival = null;
        // Window + render-service creation must run on the UI thread.
        await _application.ExecuteOnUIThreadAsync(() =>
        {
            var key = windowShell ?? (contentViewModel as IWindowAware)?.WindowShellKey;
            var window = _shells.Create(key);
            var shell = window as WindowBase;

            // Title/size FIRST: the platform worker sizes the OS window from them during AttachContextAndInitialize
            // (SetWindow), so they must be set before init.
            if (shell != null && contentViewModel is IWindowAware aware)
            {
                if (!string.IsNullOrEmpty(aware.Title)) shell.Title = aware.Title;
                if (aware.Width > 0) shell.ClientWidth = aware.Width;
                if (aware.Height > 0) shell.ClientHeight = aware.Height;
            }

            // Initialize before setting content, or the content misses the theme pass and layout. No AddWindow: init's
            // WindowCreatedEvent already creates the render service.
            window.Show();   // attaches + initializes if needed, and activates itself on first display (Window.Show)
            if (shell != null)
            {
                // So the shell's OWN bindings resolve too (a caption slot, a command bar authored on the window). The
                // view keeps its own explicit DataContext from the ViewLocator.
                shell.DataContext = contentViewModel;
                shell.Content = _viewLocator.ResolveView(contentViewModel);   // load the chosen content into the live shell
            }
            _windows[contentViewModel] = window;
            window.Closed += OnWindowClosed;
            created = window;
            if (!window.IsDisplayed && window.State != WindowState.Minimized && !_application.DisableRendering)
            {
                arrival = new WindowArrival(window);
            }
        });
        if (arrival == null)
        {
            return created;
        }

        try
        {
            await arrival.Settled.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return created;
        }

        return arrival.ClosedFirst
            ? throw new InvalidOperationException("The window closed before it came on screen.")
            : created;
    }

    public object TryActivateExisting(System.Type contentViewModelType)
    {
        foreach (var pair in _windows)
        {
            if (pair.Key.GetType() != contentViewModelType) continue;
            var window = pair.Value;
            _application.ExecuteOnUIThread(() =>
            {
                (window as WindowBase)?.Activate();   // restore-if-minimized + bring to the foreground
                _application.SetActiveWindow(window);
            });
            return pair.Key;
        }
        return null;
    }

    // Nobody waits on a window that has closed, so a view model that fails on the way out is reported here.
    private static async void Observe(Task leaving)
    {
        if (leaving == null) return;
        try
        {
            await leaving;
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Error($"A view model failed leaving its window: {ex}");
        }
    }

    public void CloseWindow(object contentViewModel)
    {
        if (contentViewModel == null || !_windows.TryGetValue(contentViewModel, out var window)) return;
        _application.ExecuteOnUIThread(window.Close);
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        var window = (IWindow)sender;
        window.Closed -= OnWindowClosed;
        foreach (var pair in _windows)
        {
            if (ReferenceEquals(pair.Value, window))
            {
                _windows.Remove(pair.Key);
                Observe(_application.Navigation?.LeaveWindowAsync(pair.Key));
                return;
            }
        }
    }
}
