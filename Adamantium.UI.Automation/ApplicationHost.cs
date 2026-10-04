using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

/// <summary>A running application: work goes to its loop thread through the dispatcher, and it has settled once whole
/// frames have run after the work.</summary>
public sealed class ApplicationHost : IAutomationHost
{
    private const int SettleFrames = 2;

    private readonly UIApplication _application;

    public ApplicationHost(UIApplication application)
    {
        _application = application;
    }

    public IReadOnlyList<IWindow> Windows => _application.Windows;

    public async Task<T> RunOnLoopAsync<T>(Func<T> work, TimeSpan timeout)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _application.Dispatcher.Post(() =>
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
        var frames = 0;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFrame(object sender, EventArgs e)
        {
            if (++frames < SettleFrames)
            {
                LoopSignal.Request();
                return;
            }

            settled.TrySetResult();
        }

        _application.CycleFinished += OnFrame;
        try
        {
            LoopSignal.Request();
            await settled.Task.WaitAsync(timeout);
        }
        finally
        {
            _application.CycleFinished -= OnFrame;
        }
    }

    public Task ShutdownAsync()
    {
        _application.ShutDown();
        return Task.CompletedTask;
    }
}
