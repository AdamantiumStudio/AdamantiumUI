using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;

namespace Adamantium.UI.Automation;

/// <summary>Windows built by a test in its own process, with no application loop and no GPU: the test's thread is the
/// loop, and settling is done here - layout passes, bindings and queued work, then the release of what was let go, as
/// the application's idle frames release it - instead of waiting for frames.</summary>
public sealed class HeadlessHost : IAutomationHost
{
    private const int SettlePasses = 3;

    private readonly List<IWindow> _windows;

    // The application the windows are listed in, until the host is done with them.
    private readonly IUIApplication _application = UIAppContext.Current;

    /// <summary>Hosts <paramref name="windows"/> with a pointer of its own: a capture an earlier test left on the shared
    /// mouse device is let go. Under a platform - a <see cref="HeadlessApplication"/> - a window not yet initialized is
    /// initialized as the application would: its markup built, its view-model made, its theme applied. Every window is
    /// listed among the current application's windows, as a platform window lists itself, so a theme swap and a new
    /// language reach it - until <see cref="Release"/>.</summary>
    public HeadlessHost(IEnumerable<IWindow> windows)
    {
        _windows = [.. windows];
        Mouse.Pointer.Capture(null);
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

        foreach (var window in _windows)
        {
            _application?.AddWindow(window);
        }
    }

    /// <summary>Takes the windows off the application's list, as a platform window leaves it when closed.</summary>
    public void Release()
    {
        foreach (var window in _windows)
        {
            _application?.RemoveWindow(window);
        }
    }

    public IReadOnlyList<IWindow> Windows => _windows;

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

        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;
}
