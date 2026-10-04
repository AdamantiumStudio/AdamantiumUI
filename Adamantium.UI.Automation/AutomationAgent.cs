using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

/// <summary>Lets a test or a tool in another process drive this running application, through a named pipe only the
/// current user can open: one client at a time, one request per line, each carried out on the loop thread.</summary>
public sealed class AutomationAgent : IDisposable
{
    private readonly string _pipeName;
    private readonly AutomationExecutor _executor;
    private readonly CancellationTokenSource _stop = new();

    public AutomationAgent(UIApplication application, string pipeName)
    {
        _pipeName = pipeName;
        _executor = new AutomationExecutor(new ApplicationHost(application));
    }

    /// <summary>Starts listening, on a thread of its own.</summary>
    public void Start() => _ = Task.Run(ServeAsync);

    /// <summary>Stops listening; a request being carried out is answered first.</summary>
    public void Dispose() => _stop.Cancel();

    private async Task ServeAsync()
    {
        Serilog.Log.Information("Automation agent listening on pipe {Pipe}", _pipeName);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                await ServeClientAsync(pipe);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Automation agent on pipe {Pipe} stopped", _pipeName);
        }
    }

    private async Task ServeClientAsync(Stream pipe)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        try
        {
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                AutomationReply reply;
                try
                {
                    reply = await _executor.ExecuteAsync(AutomationProtocol.ReadRequest(line));
                }
                catch (JsonException e)
                {
                    reply = AutomationReply.Failed($"Not a request: {e.Message}");
                }

                await writer.WriteLineAsync(AutomationProtocol.Write(reply));
            }
        }
        catch (IOException)
        {
        }
    }
}
