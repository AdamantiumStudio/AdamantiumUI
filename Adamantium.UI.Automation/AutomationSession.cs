using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private static readonly object EnvironmentLock = new();

    private readonly IAutomationChannel _channel;
    private Process _process;
    private bool _closeOnDispose;
    private string _dataFolder;

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
        var data = options.FreshData ? Directory.CreateTempSubdirectory("adam-data-").FullName : null;
        Process process;
        try
        {
            process = Start(path, options, pipe, data) ?? throw new AutomationException($"{path} did not start.");
        }
        catch
        {
            DeleteData(data);
            throw;
        }

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
                _closeOnDispose = options.CloseOnDispose,
                _dataFolder = data
            };
            await session.Find(By.Type(AutomationControlType.Window)).WaitForAsync(timeout);
            return session;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            DeleteData(data);
            throw;
        }
    }

    /// <summary>Whether an action that leaves new entries in the <see cref="ErrorJournal"/> still succeeds. Off: a quiet
    /// failure fails the step; turn it on where the error is what is being checked.</summary>
    public bool AllowErrors { get; set; }

    /// <summary>The element <paramref name="by"/> finds in any window. Looked up when it is used, not now.</summary>
    public AutomationElement Find(By by) => new(this, [by]);

    /// <summary>The element a selector path written as text finds: <c>id=Canvas&gt;name=Mix/type=Thumb,name=Out</c>.</summary>
    /// <exception cref="AutomationException">The path is not well formed.</exception>
    public AutomationElement Find(string selector)
    {
        try
        {
            return new AutomationElement(this, By.ParsePath(selector));
        }
        catch (FormatException e)
        {
            throw new AutomationException(e.Message);
        }
    }

    /// <summary>Presses <paramref name="keys"/> - chords apart by spaces - in the window that has the keyboard focus.</summary>
    public Task PressKeysAsync(string keys) => RunAsync(new AutomationRequest { Command = AutomationCommand.Key, Value = keys });

    /// <summary>Types <paramref name="text"/> into the element that has the keyboard focus.</summary>
    public Task TypeAsync(string text) => RunAsync(new AutomationRequest { Command = AutomationCommand.Type, Value = text });

    /// <summary>A picture of the first window, written to <paramref name="path"/> as PNG; the full path it was written to.</summary>
    public async Task<string> ShotAsync(string path) =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Shot, Value = path })).Text;

    /// <summary>What can be acted on but has no name, under <paramref name="selector"/> or in every window.</summary>
    public async Task<IReadOnlyList<ElementInfo>> UnnamedAsync(string selector = null) =>
        (await RunAsync(new AutomationRequest { Command = AutomationCommand.Unnamed, Target = selector })).Elements;

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

    /// <summary>A finger in the middle of each of <paramref name="targets"/> - up to ten - put down together and lifted
    /// together, by input simulated inside the application; or, with <paramref name="throughSystem"/>, handed to the
    /// system's own pointer input, refused unless every one lands on its element's window on top of the desktop.</summary>
    public Task TapTogetherAsync(IReadOnlyList<AutomationElement> targets, bool throughSystem = false) =>
        FingersAsync(targets, null, [], throughSystem);

    /// <summary>A finger on each of <paramref name="targets"/> held still past the holding time and lifted, as
    /// <see cref="TapTogetherAsync"/> puts them down.</summary>
    public Task HoldTogetherAsync(IReadOnlyList<AutomationElement> targets, bool throughSystem = false) =>
        FingersAsync(targets, null, [AutomationExecutor.HoldGesture], throughSystem);

    /// <summary>A finger on each of <paramref name="targets"/> drawn from its middle by <paramref name="dx"/>,
    /// <paramref name="dy"/> of its own units, all at once, as <see cref="TapTogetherAsync"/> puts them down.</summary>
    public Task TouchDragTogetherAsync(IReadOnlyList<AutomationElement> targets, double dx, double dy, bool throughSystem = false) =>
        FingersAsync(targets, FormattableString.Invariant($"{dx},{dy}"), [], throughSystem);

    private Task FingersAsync(IReadOnlyList<AutomationElement> targets, string offset, string[] options, bool throughSystem) =>
        RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Touches,
            Targets = [.. targets.Select(target => target.Selector)],
            Value = offset,
            Properties = [.. options, .. throughSystem ? [AutomationExecutor.SystemInput] : Array.Empty<string>()]
        });

    /// <summary>The element with keyboard focus, the windows and their open popups, as text.</summary>
    public async Task<string> StateAsync() => (await RunAsync(new AutomationRequest { Command = AutomationCommand.State })).Text;

    /// <summary>Collects all garbage in the application, then its managed heap and private memory, as text.</summary>
    public async Task<string> MemoryAsync() => (await RunAsync(new AutomationRequest { Command = AutomationCommand.Memory })).Text;

    /// <summary>Moves the application onto <paramref name="theme"/> and <paramref name="variant"/>, either null to keep it,
    /// once the swap has settled. Returns the theme and variant in use on the first line, then a line per theme with its
    /// variants.</summary>
    public async Task<string> ThemeAsync(string theme = null, string variant = null) =>
        (await RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Theme,
            Value = theme,
            Properties = variant == null ? null : [variant]
        })).Text;

    /// <summary>Sends <paramref name="request"/> as it is and returns the reply as it came, a failure included.</summary>
    public Task<AutomationReply> SendAsync(AutomationRequest request) => _channel.SendAsync(request);

    /// <summary>Closes the connection; an application the session started is closed with it, unless told otherwise.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_process != null && _closeOnDispose && !_process.HasExited)
        {
            try
            {
                await _channel.SendAsync(new AutomationRequest { Command = AutomationCommand.Shutdown });
                await _process.WaitForExitAsync().WaitAsync(ExitTimeout);
            }
            catch (Exception e) when (e is TimeoutException or AutomationException or IOException or ObjectDisposedException)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                await _process.WaitForExitAsync();
            }
        }

        await _channel.DisposeAsync();
        if (_process?.HasExited ?? true)
        {
            DeleteData(_dataFolder);
        }

        _process?.Dispose();
    }

    private static void DeleteData(string folder)
    {
        if (folder == null)
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Process Start(string path, LaunchOptions options, string pipe, string data)
    {
        var variables = new Dictionary<string, string>(options.Environment) { [AutomationProtocol.PipeVariable] = pipe };
        if (data != null)
        {
            variables[ApplicationData.FolderVariable] = data;
        }

        var start = new ProcessStartInfo(path, options.Arguments ?? string.Empty)
        {
            WorkingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(path)
        };

        if (options.CloseOnDispose)
        {
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            foreach (var variable in variables)
            {
                start.Environment[variable.Key] = variable.Value;
            }

            var process = Process.Start(start);
            if (process != null)
            {
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            return process;
        }

        start.UseShellExecute = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        lock (EnvironmentLock)
        {
            var before = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                foreach (var variable in variables)
                {
                    Environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }

                return Process.Start(start);
            }
            finally
            {
                foreach (var variable in before)
                {
                    Environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }
        }
    }

    internal async Task<AutomationReply> RunAsync(AutomationRequest request)
    {
        request.AllowErrors |= AllowErrors;
        var reply = await _channel.SendAsync(request);
        return reply.Ok ? reply : throw new AutomationException(reply.Error);
    }
}
