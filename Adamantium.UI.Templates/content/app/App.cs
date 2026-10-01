using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Universes;

namespace AdamantiumApp;

public class App : MultiverseApplication
{
    // Set here rather than in Program.cs: the designer creates the application the same way and previews in this theme.
    public App()
    {
        StartupTheme = "THEME_NAME";
    }

    // View-models get their dependencies from here: containerRegistry.RegisterSingleton<IService, Service>();
    protected override void RegisterServices(IContainerRegistry containerRegistry)
    {
        base.RegisterServices(containerRegistry);
    }
}
