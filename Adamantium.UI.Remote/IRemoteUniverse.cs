using Adamantium.Multiverse.Remote;

namespace Adamantium.UI.Remote;

/// <summary>A universe of another process shown in a panel. Disposed on the panel's thread, it lets go of the panel and
/// of the connection, and the host's process ends.</summary>
public interface IRemoteUniverse : IDisposable
{
    /// <summary>Launches the game at the panel's size, on the GPU the panel draws on when the host has it too; done once
    /// it plays. Fails with the reason when the host cannot load it.</summary>
    Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken);

    /// <summary>Stops the universe; done once it stopped.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>The scene as the game has it now, as a scene file's text.</summary>
    Task<string> CaptureSceneAsync(CancellationToken cancellationToken);

    /// <summary>The last frame drawn, as a PNG.</summary>
    Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken);

    /// <summary>Done once the universe stopped or the host's process went away.</summary>
    Task Ended { get; }
}
