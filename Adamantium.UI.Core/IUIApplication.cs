using Adamantium.Navigation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core;

public interface IUIApplication
{
    IWindow MainWindow { get; set; }

    IWindow ActiveWindow { get; }

    IReadOnlyList<IWindow> Windows { get; }

    /// <summary>The application-wide navigation service (region/view + window navigation). A permanent member; view models
    /// inject <see cref="INavigationService"/>, controls/adapters reach it here.</summary>
    INavigationService Navigation { get; }

    IResourceManager ResourceManager { get; }

    IThemeManager ThemeManager { get; }
    
    IGraphicsContext GraphicsContext { get; }
    
    IDispatcher Dispatcher { get; }
    
    IUIContext UIContext { get; }

    void AddWindow(IWindow window);
    
    void RemoveWindow(IWindow window);
    
    void SetActiveWindow(IWindow window);
    
    void InactivateWindow(IWindow window);

    public void ExecuteOnUIThread(Action action);

    public Task ExecuteOnUIThreadAsync(Action action);

    /// <summary>The environment variable a process started with a right to bring its window to the foreground finds it
    /// in: set it, to a <see cref="CreateActivationTokenAsync"/>, on a process this one starts.</summary>
    const string ActivationTokenVariable = "XDG_ACTIVATION_TOKEN";

    /// <summary>A right to bring a window to the foreground that this process hands to another - one running, or one it
    /// starts. A process cannot take the foreground by itself: the system keeps it for the one the user works in. The
    /// right is the one this process was given and has not used yet, or else one vouched for by the user's last input
    /// in its active window; it holds for a short while and once. Null when there is none to give.</summary>
    Task<string> CreateActivationTokenAsync() => Task.FromResult<string>(null);

    /// <summary>Gives the application the right to bring its window to the foreground that another process handed over
    /// (that one's <see cref="CreateActivationTokenAsync"/>): the next window brought to the foreground takes it. A
    /// process started with one (<see cref="ActivationTokenVariable"/>) is given it as the application starts.</summary>
    void AcceptActivationToken(string token)
    {
    }

    /// <summary>Takes the right <see cref="AcceptActivationToken"/> gave, once; null when there is none.</summary>
    string TakeActivationToken() => null;

    /// <summary>Shows the splash window as the start shows it - on a thread, a dispatcher and a connection to the desktop
    /// of its own - while the application runs. It stays until its own close button closes it, which cancels nothing.
    /// Nothing happens when the application has no splash window, or the platform shows none - nor by default, for an
    /// application with no desktop.</summary>
    void ShowSplash()
    {
    }

    /// <summary>How often the loop updates at most, in hertz; an application with no loop of its own keeps 120.</summary>
    uint UpdateRate
    {
        get => DefaultUpdateRate;
        set { }
    }

    /// <summary>The <see cref="UpdateRate"/> of an application that sets none.</summary>
    const uint DefaultUpdateRate = 120;

    /// <summary>
    /// Enables or disables fixed framerate
    /// </summary>
    Boolean IsFixedTimeStep { get; set; }

    /// <summary>
    /// Gets or set time step for limitation of rendering frequency
    /// <remarks>value must be in seconds</remarks>
    /// </summary>
    public Double TimeStep { get; }

    /// <summary>
    /// Desired number of frames per second
    /// </summary>
    public UInt32 DesiredFPS { get; set; }
    
    public bool DisableRendering { get; set; }
}
