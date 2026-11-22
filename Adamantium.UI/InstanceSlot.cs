using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Adamantium.UI;

/// <summary>One of a few places the user's processes take by name - a copy of an application among those allowed to
/// run (<see cref="UIApplication.MaxInstances"/>), the editor holding a solution: a named mutex of the user's, in every
/// session of theirs, held by a thread of its own until it is given up. A process that ends - closed or crashed - leaves
/// its mutex abandoned, and the next one takes it, so no place is lost. A process with no place asks one that holds a
/// place (<see cref="AskHolderAsync"/>) over a pipe the holder listens on (<see cref="StartListening"/>).</summary>
public sealed class InstanceSlot : IDisposable
{
    private const byte Ask = 1;
    private const int MaxLength = 1 << 20;
    private static readonly TimeSpan Reach = TimeSpan.FromSeconds(2);

    private readonly ManualResetEventSlim _released = new();
    private Thread _holder;
    private string _pipeName;
    private CancellationTokenSource _listening;
    private bool _disposed;

    private InstanceSlot()
    {
    }

    /// <summary>Another process asked this one, the holder, with what it was started with: raised on a thread of the
    /// pool, once <see cref="StartListening"/> was called.</summary>
    public event EventHandler<InstanceAskedEventArgs> Asked;

    /// <summary>Takes a free place of the <paramref name="count"/> named <paramref name="name"/>; null when every one
    /// is taken - by another process, or by this one.</summary>
    public static InstanceSlot TryTake(string name, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var slot = new InstanceSlot();
        using var settled = new ManualResetEventSlim();
        var index = -1;
        var holder = new Thread(() =>
        {
            using var mutex = TakeFree(name, count, out index);
            settled.Set();
            if (mutex == null)
            {
                return;
            }

            slot._released.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = "AdamantiumInstanceSlot"
        };
        holder.Start();
        settled.Wait();
        slot._holder = holder;
        if (index < 0)
        {
            return null;
        }

        slot._pipeName = CreateName(name, index);
        return slot;
    }

    /// <summary>Whether the one place named <paramref name="name"/> is taken - by another process, or by this one.
    /// </summary>
    public static bool IsTaken(string name)
    {
        using var slot = TryTake(name);
        return slot == null;
    }

    /// <summary>Asks a process holding one of the <paramref name="count"/> places named <paramref name="name"/>, handing
    /// it <paramref name="arguments"/>, the folder they are relative to and <paramref name="activationToken"/> - the right
    /// to come to the front, where the system hands it so; true when one took them.</summary>
    public static async Task<bool> AskHolderAsync(string name, int count, IReadOnlyList<string> arguments,
        string workingDirectory, string activationToken, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var ask = WriteAsk(arguments, workingDirectory, activationToken);
        for (var index = 0; index < count; index++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", CreateName(name, index), PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var reach = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                reach.CancelAfter(Reach);
                await pipe.ConnectAsync(reach.Token);
                await pipe.WriteAsync(ask, reach.Token);
                await pipe.FlushAsync(reach.Token);
                var answer = new byte[1];
                if (await pipe.ReadAsync(answer, reach.Token) == 1 && answer[0] == Ask)
                {
                    return true;
                }
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested &&
                                              exception is IOException or OperationCanceledException
                                                  or UnauthorizedAccessException or TimeoutException)
            {
                // No holder of this place listening - the next one may be.
            }
        }

        return false;
    }

    /// <summary>Listens for processes asking this one, the holder; <see cref="Asked"/> tells what they handed.</summary>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listening != null)
        {
            return;
        }

        _listening = new CancellationTokenSource();
        _ = ListenAsync(_pipeName, _listening.Token);
    }

    /// <summary>Gives the place up, done by the time it returns: the next process finds it free.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;
        _released.Set();
        _holder.Join();
    }

    /// <summary>The name of the mutex of place <paramref name="index"/> of those named <paramref name="name"/>, and of the
    /// pipe its holder listens on.</summary>
    internal static string CreateName(string name, int index) =>
        $"adamantium-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)), 0, 12).ToLowerInvariant()}-{index}";

    /// <summary>The mutexes are the user's, in every session of theirs.</summary>
    internal static NamedWaitHandleOptions Options => new() { CurrentUserOnly = true, CurrentSessionOnly = false };

    private static Mutex TakeFree(string name, int count, out int index)
    {
        for (index = 0; index < count; index++)
        {
            var mutex = new Mutex(false, CreateName(name, index), Options, out _);
            try
            {
                if (mutex.WaitOne(0))
                {
                    return mutex;
                }
            }
            catch (AbandonedMutexException)
            {
                return mutex;
            }

            mutex.Dispose();
        }

        index = -1;
        return null;
    }

    private async Task ListenAsync(string name, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                if (await ReadAskAsync(pipe, cancellation) is not { } asked)
                {
                    continue;
                }

                byte[] answer = [Ask];
                await pipe.WriteAsync(answer, cancellation);
                await pipe.FlushAsync(cancellation);
                Asked?.Invoke(this, asked);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Logger.Warning("Another process asking this one was not answered: {Reason}", exception.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            }
        }
    }

    // An ask: its mark, the token, the folder, then how many arguments and each of them - every text its UTF-8 length
    // first.
    private static byte[] WriteAsk(IReadOnlyList<string> arguments, string workingDirectory, string activationToken)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(Ask);
        WriteText(stream, activationToken ?? string.Empty);
        WriteText(stream, workingDirectory ?? string.Empty);
        WriteLength(stream, arguments.Count);
        foreach (var argument in arguments)
        {
            WriteText(stream, argument ?? string.Empty);
        }

        return stream.ToArray();
    }

    private static void WriteText(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteLength(Stream stream, int length)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        stream.Write(bytes);
    }

    private static async Task<InstanceAskedEventArgs> ReadAskAsync(Stream pipe, CancellationToken cancellation)
    {
        var mark = new byte[1];
        await pipe.ReadExactlyAsync(mark, cancellation);
        if (mark[0] != Ask)
        {
            return null;
        }

        var token = await ReadTextAsync(pipe, cancellation);
        var folder = await ReadTextAsync(pipe, cancellation);
        var count = await ReadLengthAsync(pipe, cancellation);
        var arguments = new string[count];
        for (var index = 0; index < count; index++)
        {
            arguments[index] = await ReadTextAsync(pipe, cancellation);
        }

        return new InstanceAskedEventArgs(arguments, folder.Length > 0 ? folder : null, token.Length > 0 ? token : null);
    }

    private static async Task<string> ReadTextAsync(Stream pipe, CancellationToken cancellation)
    {
        var bytes = new byte[await ReadLengthAsync(pipe, cancellation)];
        await pipe.ReadExactlyAsync(bytes, cancellation);
        return Encoding.UTF8.GetString(bytes);
    }

    // A length past what a command line holds is no ask of ours.
    private static async Task<int> ReadLengthAsync(Stream pipe, CancellationToken cancellation)
    {
        var bytes = new byte[4];
        await pipe.ReadExactlyAsync(bytes, cancellation);
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return length is >= 0 and <= MaxLength ? length : throw new InvalidDataException("Not an ask: length " + length);
    }
}

/// <summary>What a process asking the holder of an <see cref="InstanceSlot"/> handed it.</summary>
public sealed class InstanceAskedEventArgs(IReadOnlyList<string> arguments, string workingDirectory,
    string activationToken) : EventArgs
{
    /// <summary>The asking process's command line, without the program.</summary>
    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>The folder the asking process ran in - what a relative path among <see cref="Arguments"/> is relative
    /// to; null when none was handed.</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>The right to come to the front, where the system hands one over (Wayland's activation token); null where
    /// it hands it otherwise - Windows lets the asker give it up before asking - or there is none.</summary>
    public string ActivationToken { get; } = activationToken;
}
