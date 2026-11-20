using Adamantium.Core.DependencyInjection;

namespace Adamantium.UI.Remote;

/// <summary>Registers the application's <see cref="IRemoteUniverseService"/>.</summary>
public static class RemoteUniverseRegistration
{
    public static IContainerRegistry AddRemoteUniverses(this IContainerRegistry registry) =>
        registry.RegisterSingleton<IRemoteUniverseService, RemoteUniverseService>();
}
