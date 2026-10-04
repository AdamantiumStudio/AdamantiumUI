using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

internal sealed class InProcessChannel : IAutomationChannel
{
    private readonly AutomationExecutor _executor;

    public InProcessChannel(AutomationExecutor executor)
    {
        _executor = executor;
    }

    public Task<AutomationReply> SendAsync(AutomationRequest request) => _executor.ExecuteAsync(request);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
