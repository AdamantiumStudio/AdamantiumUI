using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Diagnostics;

namespace Adamantium.UI.Automation;

/// <summary>A running application: work goes to its loop thread through the dispatcher, and it has settled once the
/// application reports idle.</summary>
public sealed class ApplicationHost : IAutomationHost
{
    private readonly UIApplication _application;
    private readonly IApplicationDiagnostics _diagnostics;

    public ApplicationHost(UIApplication application)
    {
        _application = application;
        _diagnostics = application.UIContext.Resolve<IApplicationDiagnostics>();
    }

    public IReadOnlyList<IWindow> Windows => _application.Windows;

    public bool IsStarting => _application.IsStarting;

    public string DescribeDeviceMemory() =>
        _application.Container.Resolve<IGraphicsDeviceService>()?.MainGraphicsDevice is { } main
            ? DeviceMemoryCensus.Take(main).Report()
            : null;

    public async Task<T> RunOnLoopAsync<T>(Func<T> work, TimeSpan timeout)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopSignal.PostAwaited(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });

        return await done.Task.WaitAsync(timeout);
    }

    public async Task WaitForIdleAsync(TimeSpan timeout)
    {
        try
        {
            await _diagnostics.WaitForIdleAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new AutomationException(
                $"The application did not go idle within {timeout.TotalSeconds:0.#} s; it waits on " +
                $"{_diagnostics.IdleBlocker ?? "nothing it can name"}.");
        }
    }

    public Task ShutdownAsync()
    {
        _application.ShutDown();
        return Task.CompletedTask;
    }
}
