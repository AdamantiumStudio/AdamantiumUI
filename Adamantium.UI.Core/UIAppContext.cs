namespace Adamantium.UI.Core;

public class UIAppContext
{
    public static IUIApplication Current { get; private set; }
    
    public static IWindowPlatformService PlatformService { get; private set; }

    public static void Initialize(IUIApplication app, IWindowPlatformService platformService)
    {
        Current ??= app;
        PlatformService ??= platformService;
    }

    /// <summary>Makes <paramref name="app"/> and <paramref name="platformService"/> the current ones in place of those
    /// before - for a process that runs applications one after another, as a test run does.</summary>
    public static void Replace(IUIApplication app, IWindowPlatformService platformService)
    {
        Current = app;
        PlatformService = platformService;
    }
}