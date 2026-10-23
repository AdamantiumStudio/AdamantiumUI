using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core;

public interface IUIContext
{
    T Resolve<T>(string name = "");
    
    object Resolve(Type type, string name = "");
    
    public IThemeEngine ThemeEngine { get; }
    
    public IUIApplication UIApplication { get; }

    /// <summary>The dispatcher on whose thread the windows of this context talk to the desktop: the application's, or the
    /// splash window's own.</summary>
    public Dispatcher.IDispatcher Dispatcher => UIApplication.Dispatcher;
}