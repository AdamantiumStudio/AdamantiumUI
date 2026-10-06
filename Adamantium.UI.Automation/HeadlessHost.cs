using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Rendering;
using Adamantium.UI.Extensions;

namespace Adamantium.UI.Automation;

/// <summary>Windows built by a test in its own process, with no application loop and no GPU: the test's thread is the
/// loop, and settling is done here - layout passes, bindings and queued work - instead of waiting for frames.</summary>
public sealed class HeadlessHost : IAutomationHost
{
    private const int SettlePasses = 3;

    private readonly List<IWindow> _windows;

    /// <summary>Hosts <paramref name="windows"/> with a pointer of its own: a capture an earlier test left on the shared
    /// mouse device is let go. Under a platform - a <see cref="HeadlessApplication"/> - a window not yet initialized is
    /// initialized as the application would: its markup built, its view-model made, its theme applied.</summary>
    public HeadlessHost(IEnumerable<IWindow> windows)
    {
        _windows = [.. windows];
        MouseDevice.CurrentDevice.Capture(null);
        if (UIAppContext.PlatformService != null && UIAppContext.Current?.UIContext is { } context)
        {
            foreach (var window in _windows)
            {
                if (window is IRootVisualComponent { UIContext: null } root)
                {
                    root.AttachContextAndInitialize(context);
                }
            }
        }
    }

    public IReadOnlyList<IWindow> Windows => _windows;

    public IVisualRenderer Renderer => null;

    public Task<T> RunOnLoopAsync<T>(Func<T> work, TimeSpan timeout) => Task.FromResult(work());

    public Task WaitForIdleAsync(TimeSpan timeout)
    {
        for (var pass = 0; pass < SettlePasses; pass++)
        {
            LoopSignal.Drain();
            foreach (var window in _windows)
            {
                (window as FundamentalUIComponent)?.ApplyCurrentTheme();
                WindowExtension.UpdateTree(window);
                BindingUpdateQueue.Flush();
                window.LayoutPopups();
            }
        }

        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;
}
