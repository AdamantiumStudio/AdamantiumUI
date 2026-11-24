using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Remote;

/// <summary>A universe of another process, shown in a panel (<see cref="ShowIn"/>, or <see cref="RemoteUniverseHost"/>
/// in markup). Disposed on the panel's thread, it lets go of the panel and of the connection, and the host's process
/// ends.</summary>
public interface IRemoteUniverse : IDisposable
{
    /// <summary>Shows the universe in <paramref name="panel"/> - its frames drawn there, the panel's size, pointer, focus
    /// and input given to it - and nowhere else; null shows it nowhere. A surface another panel had is not imported
    /// again: the host is asked for a new one. On the panel's thread.</summary>
    void ShowIn(RenderTargetPanel panel);

    /// <summary>The panel the universe is shown in; null while it is shown nowhere.</summary>
    RenderTargetPanel Panel { get; }

    /// <summary>Launches the game at the size of the panel it is shown in, on the GPU the panel draws on when the host
    /// has it too; done once it plays. Fails with the reason when the host cannot load it.</summary>
    Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken);

    /// <summary>Stops the universe; done once it stopped.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>The scene as the game has it now, as a scene file's text.</summary>
    Task<string> CaptureSceneAsync(CancellationToken cancellationToken);

    /// <summary>The last frame drawn, as a PNG.</summary>
    Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken);

    /// <summary>Asks for the world drawn as it stands, with no step of the game: at a stop of a game a debugger holds
    /// (launched <see cref="UniverseLaunch.IsDebugged"/>), for its view to show where it stands. Not waited for.</summary>
    void DrawWhereItStands();

    /// <summary>Done once the universe stopped or the host's process went away.</summary>
    Task Ended { get; }
}
