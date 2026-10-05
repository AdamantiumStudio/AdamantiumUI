using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

internal sealed class PipeChannel : IAutomationChannel
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _turn = new(1, 1);

    private PipeChannel(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    }

    public static async Task<PipeChannel> ConnectAsync(string pipeName, TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds);
        }
        catch (TimeoutException)
        {
            await pipe.DisposeAsync();
            throw new AutomationException($"No agent answered on pipe '{pipeName}' within {timeout.TotalSeconds:0.#} s.");
        }

        return new PipeChannel(pipe);
    }

    public async Task<AutomationReply> SendAsync(AutomationRequest request)
    {
        await _turn.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(AutomationProtocol.Write(request));
            var line = await _reader.ReadLineAsync();
            return line == null
                ? AutomationReply.Failed("The agent closed the pipe; the application has exited.")
                : AutomationProtocol.ReadReply(line);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _writer.DisposeAsync();
        await _pipe.DisposeAsync();
        _turn.Dispose();
    }
}
