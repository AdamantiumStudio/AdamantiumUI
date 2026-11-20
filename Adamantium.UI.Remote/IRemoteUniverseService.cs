namespace Adamantium.UI.Remote;

/// <summary>Connects to universes that run in other processes (see <see cref="RemoteUniverseRegistration.AddRemoteUniverses"/>).
/// </summary>
public interface IRemoteUniverseService
{
    /// <summary>Connects to the universe host serving on <paramref name="pipeName"/>; the universe is shown nowhere until
    /// <see cref="IRemoteUniverse.ShowIn"/>.</summary>
    Task<IRemoteUniverse> ConnectAsync(string pipeName, CancellationToken cancellationToken);
}
