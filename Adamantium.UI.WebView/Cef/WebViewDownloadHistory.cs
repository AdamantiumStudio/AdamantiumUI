using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The downloads listed, kept in the profile's folder so the list outlives the run, as a browser's does. Written whole
/// - when one is listed, changes state or is forgotten, never as bytes come in - off the UI loop, the latest list
/// winning. One run at a time writes it: a second on the same profile reads it and leaves it be. A record that cannot be
/// read is skipped; a file that cannot be read at all is set aside beside it, never written over.
/// </summary>
internal sealed class WebViewDownloadHistory : IDisposable
{
    private const string FileName = "downloads.json";
    private const string LockName = "downloads.lock";
    private const string DamagedSuffix = ".damaged";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly string file;
    private readonly FileStream writeLock;
    private readonly Lock pendingLock = new();
    private readonly List<WebDownload> keptDownloads = [];
    private readonly List<WebViewKeptDownload> keptRecords = [];
    private List<WebViewKeptDownload> pending;
    private Task writer = Task.CompletedTask;
    private bool writing;
    private volatile bool lastWriteFailed;

    public WebViewDownloadHistory(string profileFolder)
    {
        if (profileFolder == null)
        {
            return;
        }

        file = Path.Combine(profileFolder, FileName);
        try
        {
            Directory.CreateDirectory(profileFolder);
            writeLock = new FileStream(Path.Combine(profileFolder, LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Logger.Warning(exception, "The downloads of {Folder} are kept by another run; this one only reads them",
                profileFolder);
        }
    }

    /// <summary>Whether this run writes the file: the profile is not another run's.</summary>
    public bool Writes => writeLock != null;

    /// <summary>Whether the last write failed: the list is to be written again at the next chance.</summary>
    public bool LastWriteFailed => lastWriteFailed;

    /// <summary>The downloads kept, in the order they were listed; none when the file is missing or unreadable.</summary>
    public IReadOnlyList<WebViewKeptDownload> Load()
    {
        if (file == null || !File.Exists(file))
        {
            return [];
        }

        List<JsonElement> records;
        try
        {
            records = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                              or NotSupportedException)
        {
            Serilog.Log.Logger.Warning(exception, "The downloads could not be read from {File}", file);
            SetAside();
            return [];
        }

        var kept = new List<WebViewKeptDownload>();
        foreach (var record in records ?? [])
        {
            try
            {
                if (record.Deserialize<WebViewKeptDownload>(Options) is { Address.Length: > 0 } download)
                {
                    kept.Add(download);
                }
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                Serilog.Log.Logger.Warning(exception, "A download in {File} could not be read and is left out", file);
            }
        }

        return kept;
    }

    /// <summary>Keeps <paramref name="downloads"/>, each noting what it was kept as.</summary>
    public void Save(IReadOnlyList<WebDownload> downloads)
    {
        keptDownloads.Clear();
        keptRecords.Clear();
        for (var i = 0; i < downloads.Count; i++)
        {
            var record = downloads[i].Kept();
            downloads[i].KeptAs = record;
            keptDownloads.Add(downloads[i]);
            keptRecords.Add(record);
        }

        Write();
    }

    /// <summary>Keeps <paramref name="download"/> anew among the downloads last kept, the rest as they were kept - as the
    /// application closes, when what Chromium says of the others is no longer what the next run is to list.</summary>
    public void Update(WebDownload download)
    {
        var index = keptDownloads.IndexOf(download);
        if (index < 0)
        {
            return;
        }

        keptRecords[index] = download.Kept();
        download.KeptAs = keptRecords[index];
        Write();
    }

    /// <summary>Waits for the writing under way, and lets the profile go.</summary>
    public void Dispose()
    {
        Task waiting;
        lock (pendingLock)
        {
            waiting = writer;
        }

        try
        {
            waiting.Wait();
        }
        catch (AggregateException exception)
        {
            Serilog.Log.Logger.Warning(exception, "The downloads could not be written to {File}", file);
        }

        writeLock?.Dispose();
    }

    private void Write()
    {
        if (!Writes)
        {
            return;
        }

        lock (pendingLock)
        {
            pending = [..keptRecords];
            if (!writing)
            {
                writing = true;
                writer = Task.Run(WriteWhilePending);
            }
        }
    }

    private void WriteWhilePending()
    {
        while (true)
        {
            List<WebViewKeptDownload> records;
            lock (pendingLock)
            {
                records = pending;
                pending = null;
                if (records == null)
                {
                    writing = false;
                    return;
                }
            }

            try
            {
                // Whole or not at all: a file cut off midway would lose the list.
                var writingFile = file + ".new";
                File.WriteAllText(writingFile, JsonSerializer.Serialize(records, Options));
                File.Move(writingFile, file, overwrite: true);
                lastWriteFailed = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastWriteFailed = true;
                Serilog.Log.Logger.Warning(exception, "The downloads could not be written to {File}", file);
            }
        }
    }

    private void SetAside()
    {
        if (!Writes)
        {
            return;
        }

        try
        {
            File.Move(file, file + DamagedSuffix, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Logger.Warning(exception, "The downloads file {File} could not be set aside", file);
        }
    }
}
