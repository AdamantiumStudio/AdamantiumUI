using Adamantium.Graphics.Core;
using Adamantium.Multiverse.Remote;

namespace Adamantium.UI.Remote;

internal sealed class RemoteUniverseService : IRemoteUniverseService
{
    private readonly IGraphicsDeviceService _devices;

    public RemoteUniverseService(IGraphicsDeviceService devices)
    {
        _devices = devices;
    }

    public async Task<IRemoteUniverse> ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        var device = _devices.MainGraphicsDevice?.GraphicsAdapter.DeviceUuid ?? Guid.Empty;
        var universe = new RemoteUniverse(device);
        try
        {
            var connection = await UniverseHostConnection.ConnectAsync(pipeName, universe.Viewer, cancellationToken);
            universe.Attach(connection);
            return universe;
        }
        catch
        {
            universe.Dispose();
            throw;
        }
    }
}
