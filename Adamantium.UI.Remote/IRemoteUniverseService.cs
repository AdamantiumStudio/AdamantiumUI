using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Remote;

/// <summary>Shows universes that run in other processes (see <see cref="RemoteUniverseRegistration.AddRemoteUniverses"/>).
/// </summary>
public interface IRemoteUniverseService
{
    /// <summary>Connects to the universe host serving on <paramref name="pipeName"/> and shows its frames in
    /// <paramref name="panel"/>, giving it the panel's size, focus and input. Called on the panel's thread.</summary>
    Task<IRemoteUniverse> ConnectAsync(string pipeName, RenderTargetPanel panel, CancellationToken cancellationToken);
}
