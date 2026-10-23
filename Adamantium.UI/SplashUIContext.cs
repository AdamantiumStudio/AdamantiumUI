using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Platforms;

namespace Adamantium.UI;

/// <summary>The application's context with the splash window's own connection to the desktop and its own dispatcher in
/// place of the application's.</summary>
internal sealed class SplashUIContext(IUIContext application, IApplicationPlatform platform, IDispatcher dispatcher) : IUIContext
{
    public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);

    public object Resolve(Type type, string name = "") =>
        type == typeof(IApplicationPlatform) ? platform : application.Resolve(type, name);

    public IThemeEngine ThemeEngine => application.ThemeEngine;

    public IUIApplication UIApplication => application.UIApplication;

    public IDispatcher Dispatcher => dispatcher;
}
