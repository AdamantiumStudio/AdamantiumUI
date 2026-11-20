using Adamantium.Multiverse.Remote;

namespace Adamantium.UI.Remote;

/// <summary>What the host tells a <see cref="RemoteUniverse"/>: apart from it, since the connection serves every public
/// method of the object it is given.</summary>
internal sealed class RemoteUniverseViewer : IUniverseViewer
{
    private readonly RemoteUniverse _universe;

    public RemoteUniverseViewer(RemoteUniverse universe)
    {
        _universe = universe;
    }

    public void Greet(RemoteHello hello)
    {
    }

    public void ShowSurface(RemoteSurface surface) => _universe.ShowSurface(surface);

    public void ShowFrame(ulong frame) => _universe.ShowFrame(frame);

    public void EndUniverse() => _universe.EndUniverse();
}
