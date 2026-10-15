using Adamantium.UI.Markup;
using Serilog;

namespace Adamantium.UI.Core.Markup;

/// <summary>
/// Applies a saved markup file to the running application: every view, window and control built from it takes the
/// change in place at the loop's next frame, keeping what did not change. Only what generated code reports through
/// <see cref="Track"/> takes part - a debug build with AdamantiumAumlHotReload. A version that does not build is
/// reported and the running one stays. Do not wait for <see cref="ApplyAsync"/> on the loop thread: the loop applies it.
/// </summary>
public static class MarkupHotReload
{
    private const int SaveAttempts = 20;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LoopPatience = TimeSpan.FromSeconds(10);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, TrackedMarkup> Files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FileSystemWatcher> Watchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Changed = new(StringComparer.OrdinalIgnoreCase);
    // The hash of each file's text as last taken - built from, or taken to apply. A save is told apart by what it says,
    // not by when: two saves within one tick of the file clock share a write time, and the second was lost.
    private static readonly Dictionary<string, string> Taken = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Timer SettleTimer = new(_ => ApplyChanged(), null, Timeout.Infinite, Timeout.Infinite);
    private static volatile MarkupReloadMode _mode;

    /// <summary>Raised on the loop thread once a saved file is in place in its live instances.</summary>
    public static event Action<MarkupReloadReport> Reloaded;

    /// <summary>Raised when a saved file is not applied - it does not build, or applying it failed - with why.</summary>
    public static event Action<MarkupReloadReport> Failed;

    /// <summary>When a saved change is applied. Switching back to OnSave applies what Manual gathered.</summary>
    public static MarkupReloadMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (value == MarkupReloadMode.OnSave)
            {
                SettleTimer.Change(Settle, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Tracked files changed on disk and not applied yet; in Manual mode, everything saved since the last apply.</summary>
    public static IReadOnlyList<string> ChangedFiles
    {
        get
        {
            lock (Sync)
            {
                return Changed.ToList();
            }
        }
    }

    internal static Action<Action> Dispatch { get; set; } = LoopSignal.Post;

    /// <summary>Called by generated code once <paramref name="instance"/> is built from the markup file at
    /// <paramref name="path"/>, whose text had the <see cref="MarkupHash"/> <paramref name="hash"/>. An instance made after
    /// an edit was built from the compiled markup, so it takes the edits at once. Never throws: a failure here is logged
    /// and the instance simply does not take part.</summary>
    public static void Track(object instance, string path, string hash)
    {
        if (Design.IsDesignMode || instance == null || string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            TrackedMarkup file;
            lock (Sync)
            {
                if (!Files.TryGetValue(fullPath, out file))
                {
                    var onDisk = File.Exists(fullPath) ? ReadText(fullPath, 1) : null;
                    file = new TrackedMarkup(fullPath, BuiltText(fullPath, onDisk, hash), hash);
                    Files[fullPath] = file;
                    Taken[fullPath] = HashOf(onDisk) ?? hash;
                    Watch(Path.GetDirectoryName(fullPath));
                }
            }

            var diagnostics = new List<string>();
            file.Track(instance, hash, diagnostics);
            LogProblems(fullPath, diagnostics);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(exception, "Markup hot reload: {File} is not followed", path);
        }
    }

    /// <summary>Treats <paramref name="path"/> as saved, as the watcher would; a save already applied is skipped.</summary>
    public static void NotifyChanged(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (Sync)
        {
            if (!Files.ContainsKey(fullPath))
            {
                return;
            }
        }

        // Read outside the lock, compared inside it; and what looks new is read again inside it, since the first read may
        // be older than a save taken to apply meanwhile - a write reports itself as soon as it empties the file. Unread
        // counts as new: a file the save still holds is looked at again once it settles.
        var content = HashOf(ReadText(fullPath, 1));
        lock (Sync)
        {
            if (Taken.TryGetValue(fullPath, out var taken) && taken != null
                && (content == taken || HashOf(ReadText(fullPath, 1)) == taken))
            {
                return;
            }

            Changed.Add(fullPath);
        }

        if (_mode == MarkupReloadMode.OnSave)
        {
            SettleTimer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Applies the gathered changes and <paramref name="files"/>, in either mode. Pass the files just saved: the watcher
    /// may not have reported them yet.
    /// </summary>
    /// <returns>One report per tracked file applied.</returns>
    public static async Task<IReadOnlyList<MarkupReloadReport>> ApplyAsync(IEnumerable<string> files = null)
    {
        var due = TakeChanged(files ?? []);
        return await Task.WhenAll(due.Select(file => Task.Run(() => ApplyFileAsync(file)))).ConfigureAwait(false);
    }

    internal static Task CodeUpdatedAsync()
    {
        if (Design.IsDesignMode)
        {
            return Task.CompletedTask;
        }

        List<(string Path, string Taken)> tracked;
        lock (Sync)
        {
            tracked = Files.Keys.Select(path => (path, Taken.TryGetValue(path, out var taken) ? taken : null)).ToList();
        }

        var saved = tracked.Where(file => IsNewSave(file.Path, file.Taken)).Select(file => file.Path).ToList();

        Log.Logger.Information("Markup hot reload: the application's code was updated; {Count} saved file(s) to apply", saved.Count);
        Dispatch(RefreshWindows);
        return ApplyAsync(saved);
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            foreach (var watcher in Watchers.Values)
            {
                watcher.Dispose();
            }

            Watchers.Clear();
            Files.Clear();
            Changed.Clear();
            Taken.Clear();
        }
    }

    // Unread is new too: a file the save still holds is looked at again once it settles.
    private static bool IsNewSave(string path, string taken) => taken == null || HashOf(ReadText(path, 1)) != taken;

    private static string HashOf(string text) => text == null ? null : MarkupHash.Of(text);

    private static void RefreshWindows()
    {
        foreach (var window in UIAppContext.Current?.Windows ?? [])
        {
            if (window is not IUIComponent root)
            {
                continue;
            }

            (root as IMeasurableComponent)?.InvalidateMeasure();
            foreach (var element in root.GetVisualDescendants())
            {
                (element as IMeasurableComponent)?.InvalidateMeasure();
            }
        }

        VisualTreeNotifications.RaiseStateSwapStarted();
    }

    private static string BuiltText(string path, string text, string hash)
    {
        if (!File.Exists(path))
        {
            Log.Logger.Information("Markup hot reload: {File} is not on this machine; it is not followed", path);
            return null;
        }

        if (text != null && MarkupHash.Of(text) == hash)
        {
            return text;
        }

        Log.Logger.Warning("Markup hot reload: {File} is not what the application was built from; rebuild to edit it live", path);
        return null;
    }

    private static void Watch(string directory)
    {
        if (string.IsNullOrEmpty(directory) || Watchers.ContainsKey(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, "*.auml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Changed += (_, e) => NotifyChanged(e.FullPath);
        watcher.Created += (_, e) => NotifyChanged(e.FullPath);
        watcher.Renamed += (_, e) => NotifyChanged(e.FullPath);
        watcher.EnableRaisingEvents = true;
        Watchers[directory] = watcher;
    }

    private static List<TrackedMarkup> TakeChanged(IEnumerable<string> alsoChanged)
    {
        lock (Sync)
        {
            var paths = Changed.Union(alsoChanged.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase).ToList();
            Changed.Clear();
            var due = new List<TrackedMarkup>(paths.Count);
            foreach (var path in paths)
            {
                if (Files.TryGetValue(path, out var file))
                {
                    Taken[path] = HashOf(ReadText(path, 1));
                    due.Add(file);
                }
            }

            return due;
        }
    }

    private static void ApplyChanged()
    {
        if (_mode == MarkupReloadMode.Manual)
        {
            return;
        }

        foreach (var file in TakeChanged([]))
        {
            _ = Task.Run(() => ApplyFileAsync(file));
        }
    }

    private static async Task<MarkupReloadReport> ApplyFileAsync(TrackedMarkup file)
    {
        await file.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var diagnostics = new List<string>();
            var text = ReadText(file.Path, SaveAttempts);
            var edited = text == null ? null : AumlLoader.Lower(text, null, diagnostics, file.Path);
            if (text == null)
            {
                diagnostics.Add("the file could not be read");
            }
            else if (!file.KnowsWhatWasBuilt)
            {
                diagnostics.Add("it is not what the application was built from; rebuild to edit it live");
            }

            if (edited?.Plan == null || !file.KnowsWhatWasBuilt || diagnostics.Any(IsError) || !file.EnsureBuiltPlan())
            {
                return Fail(file.Path, diagnostics);
            }

            var applied = new TaskCompletionSource<MarkupReloadReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hash = MarkupHash.Of(text);
            Dispatch(() => applied.SetResult(ApplyOnLoop(file, edited, hash, diagnostics)));
            try
            {
                return await applied.Task.WaitAsync(LoopPatience).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                diagnostics.Add("the application's loop did not take the change");
                return Fail(file.Path, diagnostics);
            }
        }
        finally
        {
            file.Gate.Release();
        }
    }

    private static MarkupReloadReport ApplyOnLoop(TrackedMarkup file, LoweredDocument edited, string hash, List<string> diagnostics)
    {
        int count;
        try
        {
            count = file.Apply(edited, hash, diagnostics);
            if (count > 0 && edited.EntityType is EntityType.StyleSet or EntityType.ResourceDictionary or EntityType.ThemeVariant)
            {
                (UIAppContext.Current?.ThemeManager as Resources.ThemeManager)?.RefreshFromMarkup();
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add(exception.Message);
            return Fail(file.Path, diagnostics);
        }

        var report = new MarkupReloadReport(file.Path, true, count, diagnostics);
        Log.Logger.Information("Markup hot reload: {File} applied to {Count} instance(s)", file.Path, count);
        LogProblems(file.Path, diagnostics);
        Raise(Reloaded, report);
        return report;
    }

    private static MarkupReloadReport Fail(string path, List<string> diagnostics)
    {
        Log.Logger.Error("Markup hot reload: {File} was not applied{NewLine}{Messages}", path, Environment.NewLine,
            string.Join(Environment.NewLine, diagnostics));
        var report = new MarkupReloadReport(path, false, 0, diagnostics);
        Raise(Failed, report);
        return report;
    }

    private static void Raise(Action<MarkupReloadReport> handlers, MarkupReloadReport report)
    {
        try
        {
            handlers?.Invoke(report);
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Markup hot reload: a handler of its report failed");
        }
    }

    private static void LogProblems(string path, List<string> diagnostics)
    {
        foreach (var message in diagnostics.Where(IsError))
        {
            Log.Logger.Warning("Markup hot reload: {File}: {Message}", path, message);
        }
    }

    private static bool IsError(string diagnostic)
    {
        return !diagnostic.StartsWith("warning:", StringComparison.Ordinal) && !diagnostic.StartsWith("info:", StringComparison.Ordinal);
    }

    private static string ReadText(string path, int attempts)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < attempts)
                {
                    Thread.Sleep(Settle);
                }
            }
        }

        return null;
    }
}
