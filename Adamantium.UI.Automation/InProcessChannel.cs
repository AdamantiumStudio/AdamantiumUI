using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

internal sealed class InProcessChannel : IAutomationChannel
{
    private readonly AutomationExecutor _executor;
    private readonly HeadlessHost _host;

    public InProcessChannel(HeadlessHost host)
    {
        _host = host;
        _executor = new AutomationExecutor(host);
    }

    public Task<AutomationReply> SendAsync(AutomationRequest request) => _executor.ExecuteAsync(request);

    public ValueTask DisposeAsync()
    {
        _host.Release();
        return ValueTask.CompletedTask;
    }
}
