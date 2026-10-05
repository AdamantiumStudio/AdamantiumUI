using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Adamantium.UI.Automation;

/// <summary>What went wrong quietly while the application was driven: broken bindings, values set by a name the element
/// has no property for, errors written to the log. A driver marks it before an action and reads what came after, so a
/// quiet failure fails the step. Installed once per process, by a session or by the agent.</summary>
public static class ErrorJournal
{
    private const int Capacity = 1000;

    private static readonly object Sync = new();
    private static readonly List<ErrorEntry> Entries = [];
    private static long _sequence;
    private static bool _installed;

    /// <summary>Starts listening; a second call does nothing. Call once the application has configured its logging, which
    /// this wraps.</summary>
    public static void Install()
    {
        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
        }

        BindingTrace.Sink += message => Add("Binding", message);
        PropertyTrace.Sink += message => Add("Property", message);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Logger(Log.Logger)
            .WriteTo.Sink(new LogJournalSink(), LogEventLevel.Error)
            .CreateLogger();
    }

    /// <summary>The sequence of the newest entry.</summary>
    public static long Mark()
    {
        lock (Sync)
        {
            return _sequence;
        }
    }

    /// <summary>The entries after <paramref name="mark"/>, oldest first.</summary>
    public static List<ErrorEntry> Since(long mark)
    {
        lock (Sync)
        {
            return [.. Entries.Where(entry => entry.Sequence > mark)];
        }
    }

    internal static void Add(string kind, string message)
    {
        lock (Sync)
        {
            Entries.Add(new ErrorEntry { Sequence = ++_sequence, Kind = kind, Message = message });
            if (Entries.Count > Capacity)
            {
                Entries.RemoveAt(0);
            }
        }
    }
}
