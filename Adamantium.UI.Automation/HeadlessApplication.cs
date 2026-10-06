using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Automation;

/// <summary>The application a headless test builds its windows in: resources, a theme with its style sets - everything a
/// window needs to be styled, laid out and driven through <see cref="AutomationSession.InProcess"/>, with no loop, no
/// window of the system and no GPU. Starting one makes it the current application; start a fresh one for each test.
/// </summary>
public sealed class HeadlessApplication : IUIApplication, IUIContext, IWindowPlatformService
{
    private readonly AdamantiumDependencyContainer _container = new();
    private readonly ThemeManager _themes;

    private HeadlessApplication()
    {
        ResourceManager = new ResourceManager();
        UIAppContext.Initialize(this, this);
        UIAppContext.Replace(this, this);
        _themes = new ThemeManager(_container);
    }

    /// <summary>Starts a headless application on the theme <typeparamref name="TTheme"/> - the one the application under
    /// test starts on - and makes it the current one.</summary>
    public static HeadlessApplication Start<TTheme>() where TTheme : ITheme, new()
    {
        var application = new HeadlessApplication();
        application.UseTheme(new TTheme());
        return application;
    }

    /// <summary>Switches to <paramref name="theme"/>, made while this application is the current one.</summary>
    public void UseTheme(ITheme theme)
    {
        _themes.AddTheme(theme.Name, theme);
        _themes.SetTheme(theme);
    }

    /// <summary>Where the application's own resources go: <see cref="IResourceManager.AddSource"/> them as the
    /// application does.</summary>
    public IResourceManager ResourceManager { get; }

    /// <summary>The themes; style sets the application adds at start go here too.</summary>
    public IThemeManager ThemeManager => _themes;

    public IWindow MainWindow { get; set; }

    public IWindow ActiveWindow => null;

    public IReadOnlyList<IWindow> Windows => [];

    public INavigationService Navigation => null;

    public IGraphicsContext GraphicsContext => null;

    public IDispatcher Dispatcher => null;

    public IUIContext UIContext => this;

    public bool IsFixedTimeStep { get; set; }

    public double TimeStep => 0;

    public uint DesiredFPS { get; set; }

    public bool DisableRendering { get; set; } = true;

    IThemeEngine IUIContext.ThemeEngine => _themes;

    IUIApplication IUIContext.UIApplication => this;

    public T Resolve<T>(string name = "") => _container.Resolve<T>(name);

    public object Resolve(Type type, string name = "") => _container.Resolve(type, name);

    /// <summary>None: a headless window has no window of the system behind it.</summary>
    public IWindowWorkerService GetWindowWorker(IUIContext uiContext) => null;

    public void AddWindow(IWindow window)
    {
    }

    public void RemoveWindow(IWindow window)
    {
    }

    public void SetActiveWindow(IWindow window)
    {
    }

    public void InactivateWindow(IWindow window)
    {
    }

    public void ExecuteOnUIThread(Action action) => action();

    public Task ExecuteOnUIThreadAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
