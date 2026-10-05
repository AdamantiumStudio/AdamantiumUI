using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

/// <summary>A connection to an application to drive: windows a test built in its own process, or a running application
/// whose agent listens on a pipe. Elements are found by <see cref="By"/> and acted on through
/// <see cref="AutomationElement"/>; every action waits for the application to settle.</summary>
public sealed class AutomationSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly IAutomationChannel _channel;

    private AutomationSession(IAutomationChannel channel)
    {
        _channel = channel;
    }

    /// <summary>Windows built in this process, with no application loop: the caller's thread is the loop, and the
    /// session lays them out itself.</summary>
    public static AutomationSession InProcess(params IWindow[] windows)
    {
        ErrorJournal.Install();
        return new AutomationSession(new InProcessChannel(new AutomationExecutor(new HeadlessHost(windows))));
    }

    /// <summary>An application whose agent listens on <paramref name="pipeName"/>, waiting for it to start if need be.</summary>
    /// <exception cref="AutomationException">No agent answered in time.</exception>
    public static async Task<AutomationSession> AttachAsync(string pipeName, TimeSpan? timeout = null) =>
        new(await PipeChannel.ConnectAsync(pipeName, timeout ?? DefaultConnectTimeout));

    /// <summary>Whether an action that leaves new entries in the <see cref="ErrorJournal"/> still succeeds. Off: a quiet
    /// failure fails the step; turn it on where the error is what is being checked.</summary>
    public bool AllowErrors { get; set; }

    /// <summary>The element <paramref name="by"/> finds in any window. Looked up when it is used, not now.</summary>
    public AutomationElement Find(By by) => new(this, [by]);

    public async Task<IReadOnlyList<ElementInfo>> FindAllAsync(By by) =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Find, Target = by.ToString() })).Elements;

    public async Task<IReadOnlyList<ElementInfo>> WindowsAsync() =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Windows })).Elements;

    public Task WaitForIdleAsync() => RunAsync(new AutomationRequest { Command = AutomationCommand.WaitIdle });

    /// <summary>The automation tree of every window as indented text; <paramref name="depth"/> 0 for all of it.</summary>
    public async Task<string> TreeAsync(int depth = 0) =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Tree, Depth = depth })).Text;

    public async Task DumpTreeAsync(string path) => await File.WriteAllTextAsync(path, await TreeAsync());

    /// <summary>The newest entry of the error journal, to ask later what came after it.</summary>
    public async Task<long> MarkAsync() => (await RunAsync(new AutomationRequest { Command = AutomationCommand.Mark })).Mark;

    /// <summary>What the error journal holds after <paramref name="mark"/>.</summary>
    public async Task<IReadOnlyList<ErrorEntry>> ErrorsSinceAsync(long mark) =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Errors, Since = mark })).Errors;

    /// <summary>The element with keyboard focus, the windows and their open popups, as text.</summary>
    public async Task<string> StateAsync() => (await RunAsync(new AutomationRequest { Command = AutomationCommand.State })).Text;

    /// <summary>Sends <paramref name="request"/> as it is and returns the reply as it came, a failure included.</summary>
    public Task<AutomationReply> SendAsync(AutomationRequest request) => _channel.SendAsync(request);

    public ValueTask DisposeAsync() => _channel.DisposeAsync();

    internal async Task<AutomationReply> RunAsync(AutomationRequest request)
    {
        request.AllowErrors |= AllowErrors;
        var reply = await _channel.SendAsync(request);
        return reply.Ok ? reply : throw new AutomationException(reply.Error);
    }
}
