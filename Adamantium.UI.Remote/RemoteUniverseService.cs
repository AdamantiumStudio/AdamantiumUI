using Adamantium.Graphics.Core;
using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Remote;

internal sealed class RemoteUniverseService : IRemoteUniverseService
{
    private readonly IGraphicsDeviceService _devices;

    public RemoteUniverseService(IGraphicsDeviceService devices)
    {
        _devices = devices;
    }

    public async Task<IRemoteUniverse> ConnectAsync(string pipeName, RenderTargetPanel panel,
        CancellationToken cancellationToken)
    {
        var device = _devices.MainGraphicsDevice?.GraphicsAdapter.DeviceUuid ?? Guid.Empty;
        var universe = new RemoteUniverse(panel, device);
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
