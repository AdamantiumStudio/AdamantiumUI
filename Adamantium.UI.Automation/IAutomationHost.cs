using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

/// <summary>Where automation runs: the windows it sees, the thread their trees live on, and when the application has
/// settled.</summary>
public interface IAutomationHost
{
    /// <summary>The open windows; read on the loop thread.</summary>
    IReadOnlyList<IWindow> Windows { get; }

    /// <summary>Runs <paramref name="work"/> on the thread the visual trees live on.</summary>
    /// <exception cref="TimeoutException">The thread did not get to it in time - the application may be frozen.</exception>
    Task<T> RunOnLoopAsync<T>(Func<T> work, TimeSpan timeout);

    /// <summary>Waits until layout, bindings and queued work have caught up with what was just done.</summary>
    Task WaitForIdleAsync(TimeSpan timeout);

    /// <summary>Closes the application.</summary>
    Task ShutdownAsync();
}
