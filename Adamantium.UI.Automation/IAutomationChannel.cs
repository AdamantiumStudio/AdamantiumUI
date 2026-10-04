using System;
using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

internal interface IAutomationChannel : IAsyncDisposable
{
    Task<AutomationReply> SendAsync(AutomationRequest request);
}
