using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation;

/// <summary>A connection to an application to drive: windows a test built in its own process, or a running application
/// whose agent listens on a pipe. Elements are found by <see cref="By"/> and acted on through
/// <see cref="AutomationElement"/>; every action waits for the application to settle.</summary>
public sealed class AutomationSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultLaunchTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    private readonly IAutomationChannel _channel;
    private Process _process;
    private bool _closeOnDispose;

    private AutomationSession(IAutomationChannel channel)
    {
        _channel = channel;
    }

    /// <summary>The application's process when the session started it; null otherwise.</summary>
    public Process Process => _process;

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

    /// <summary>Starts the application at <paramref name="executable"/> with its agent on a pipe of its own and waits for
    /// its first window. The application has to call <see cref="AutomationAgentExtensions.UseAutomationAgent"/>.</summary>
    /// <param name="executable">The application to start.</param>
    /// <param name="options">Its arguments, environment and time to start; whether disposing the session closes it.</param>
    /// <exception cref="AutomationException">It exited, or no window came, in time.</exception>
    public static async Task<AutomationSession> LaunchAsync(string executable, LaunchOptions options = null)
    {
        options ??= new LaunchOptions();
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path))
        {
            throw new AutomationException($"No application at {path}.");
        }

        var pipe = options.PipeName ?? $"adam-auto-{Guid.NewGuid():N}";
        var start = new ProcessStartInfo(path, options.Arguments ?? string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(path)
        };
        start.Environment[AutomationProtocol.PipeVariable] = pipe;
        foreach (var variable in options.Environment)
        {
            start.Environment[variable.Key] = variable.Value;
        }

        var process = Process.Start(start) ?? throw new AutomationException($"{path} did not start.");

        var timeout = options.StartTimeout ?? DefaultLaunchTimeout;
        try
        {
            var connecting = PipeChannel.ConnectAsync(pipe, timeout);
            if (await Task.WhenAny(connecting, process.WaitForExitAsync()) != connecting)
            {
                throw new AutomationException($"{Path.GetFileName(path)} exited with code {process.ExitCode} before its agent answered.");
            }

            IAutomationChannel channel;
            try
            {
                channel = await connecting;
            }
            catch (AutomationException e)
            {
                throw new AutomationException($"{e.Message} Does {Path.GetFileName(path)} call UseAutomationAgent() before it runs?");
            }

            var session = new AutomationSession(channel)
            {
                _process = process,
                _closeOnDispose = options.CloseOnDispose
            };
            await session.Find(By.Type(AutomationControlType.Window)).WaitForAsync(timeout);
            return session;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

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

    /// <summary>Runs a scenario file: commands of the automation language, one a line, as <c>adam-auto</c> takes them -
    /// <c>invoke id=Save</c>, <c>expect id=Status name="Saved."</c>, <c>wait id=Total value=40</c> - each step and what
    /// it did written to <paramref name="output"/>.</summary>
    /// <exception cref="AutomationException">A step failed; the message names the file and the line.</exception>
    public Task RunScenarioAsync(string path, TextWriter output = null) => new ScriptInterpreter(this, output).RunFileAsync(path);

    /// <summary>Runs one command of the automation language, written as on a line of a scenario.</summary>
    /// <exception cref="AutomationException">It failed.</exception>
    public Task RunCommandAsync(string command, TextWriter output = null) =>
        RunCommandAsync([.. ScriptInterpreter.Tokenize(command)], output);

    /// <summary>Runs one command of the automation language, already split into its words.</summary>
    /// <exception cref="AutomationException">It failed.</exception>
    public Task RunCommandAsync(IReadOnlyList<string> words, TextWriter output = null) =>
        new ScriptInterpreter(this, output).RunAsync(words);

    /// <summary>Closes the connection; an application the session started is closed with it, unless told otherwise.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_process != null && _closeOnDispose && !_process.HasExited)
        {
            await _channel.SendAsync(new AutomationRequest { Command = AutomationCommand.Shutdown });
            try
            {
                await _process.WaitForExitAsync().WaitAsync(ExitTimeout);
            }
            catch (TimeoutException)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        await _channel.DisposeAsync();
        _process?.Dispose();
    }

    internal async Task<AutomationReply> RunAsync(AutomationRequest request)
    {
        request.AllowErrors |= AllowErrors;
        var reply = await _channel.SendAsync(request);
        return reply.Ok ? reply : throw new AutomationException(reply.Error);
    }
}
