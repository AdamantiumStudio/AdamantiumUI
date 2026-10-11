using System;
using System.IO;
using System.Threading;

namespace Adamantium.UI.Rendering.Verification;

/// <summary>The frame verifier: while on, what a window's render cache draws with is checked against what its tree says,
/// and every part that differs is logged to <see cref="LogPath"/>. Off, it costs one read per frame.</summary>
public sealed class FrameVerification
{
    private static volatile FrameVerification active;

    private readonly object _logLock = new();
    private volatile bool _enabled;
    private int _verified;
    private int _mismatched;
    private int _skipped;
    private int _reports;

    internal static FrameVerification Active => active;

    /// <summary>Whether frames are verified; on starts a new log, off drops the verifier.</summary>
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
            Log(value
                ? "verifying"
                : $"stopped: {VerifiedFrames} frames checked, {MismatchedFrames} with alarms, {SkippedFrames} skipped");
        }
    }

    /// <summary>Verifies every Nth recorded frame; 1 verifies them all.</summary>
    public int Every { get; set; } = 1;

    /// <summary>Where the logs are written, one per session, named by the time it started.</summary>
    public string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "verify");

    /// <summary>How many differing frames a session describes in its log; later ones are only counted.</summary>
    public int MaxReports { get; set; } = 500;

    /// <summary>The log of the current session.</summary>
    public string LogPath { get; private set; }

    /// <summary>Frames checked this session.</summary>
    public int VerifiedFrames => Volatile.Read(ref _verified);

    /// <summary>Frames in which the cache and the tree parted this session.</summary>
    public int MismatchedFrames => Volatile.Read(ref _mismatched);

    /// <summary>Frames that could not be checked this session: the window drew a later record, or held the frame back.</summary>
    public int SkippedFrames => Volatile.Read(ref _skipped);

    internal void CountVerified() => Interlocked.Increment(ref _verified);

    internal void CountMismatched() => Interlocked.Increment(ref _mismatched);

    internal void CountSkipped() => Interlocked.Increment(ref _skipped);

    internal bool TakeReport() => Interlocked.Increment(ref _reports) <= MaxReports;

    internal void Log(string text)
    {
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
            catch (Exception)
            {
            }
        }
    }

    private void StartSession()
    {
        Interlocked.Exchange(ref _verified, 0);
        Interlocked.Exchange(ref _mismatched, 0);
        Interlocked.Exchange(ref _skipped, 0);
        Interlocked.Exchange(ref _reports, 0);
        LogPath = Path.Combine(Folder, $"verify-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }
}
