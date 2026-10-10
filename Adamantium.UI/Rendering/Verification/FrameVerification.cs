using System;
using System.IO;
using System.Threading;

namespace Adamantium.UI.Rendering.Verification;

/// <summary>The frame verifier: while on, a window's frames are compared with a fresh full build of the same tree, and
/// each one that differs is written to <see cref="SessionFolder"/> with a report. Off, it costs one read per frame.</summary>
public sealed class FrameVerification
{
    private static volatile FrameVerification active;

    private readonly object _logLock = new();
    private volatile bool _enabled;
    private int _verified;
    private int _mismatched;
    private int _skipped;
    private int _dumps;

    internal static FrameVerification Active => active;

    /// <summary>Whether frames are verified; on starts a new session, off frees everything the verifier holds.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (value)
            {
                StartSession();
            }

            _enabled = value;
            active = value ? this : null;
        }
    }

    /// <summary>Verifies every Nth recorded frame; 1 verifies them all.</summary>
    public int Every { get; set; } = 1;

    /// <summary>Where sessions are written; a session is a subfolder named by the time it started.</summary>
    public string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "verify");

    /// <summary>How many differing frames a session writes out in full; later ones are counted and logged.</summary>
    public int MaxDumps { get; set; } = 20;

    /// <summary>The folder of the current session.</summary>
    public string SessionFolder { get; private set; }

    /// <summary>Frames compared this session.</summary>
    public int VerifiedFrames => Volatile.Read(ref _verified);

    /// <summary>Frames that differed from the full walk this session.</summary>
    public int MismatchedFrames => Volatile.Read(ref _mismatched);

    /// <summary>Frames that could not be compared this session; <c>verify.log</c> says why.</summary>
    public int SkippedFrames => Volatile.Read(ref _skipped);

    internal void CountVerified() => Interlocked.Increment(ref _verified);

    internal void CountMismatched() => Interlocked.Increment(ref _mismatched);

    internal void CountSkipped() => Interlocked.Increment(ref _skipped);

    internal bool TakeDump() => Interlocked.Increment(ref _dumps) <= MaxDumps;

    internal void Log(string line)
    {
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(SessionFolder);
                File.AppendAllText(Path.Combine(SessionFolder, "verify.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void StartSession()
    {
        Interlocked.Exchange(ref _verified, 0);
        Interlocked.Exchange(ref _mismatched, 0);
        Interlocked.Exchange(ref _skipped, 0);
        Interlocked.Exchange(ref _dumps, 0);
        SessionFolder = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }
}
