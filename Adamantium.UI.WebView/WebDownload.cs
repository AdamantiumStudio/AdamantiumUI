using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>
/// A file a page downloads: what it is, how far it has come, and the means to pause, go on or cancel. Asked of the
/// application first (<see cref="WebViewBridge.DownloadRequested"/>), which says where to save it; once saved there it
/// is among <see cref="WebViewEngine.Downloads"/>, as are the ones an earlier run kept in the profile, which Chromium
/// knows nothing of. Its properties change as it comes in; it is used on the UI thread.
/// </summary>
public sealed unsafe class WebDownload : INotifyPropertyChanged
{
    private const int NameLimit = 200;

    private readonly Action<WebDownload> saved;
    private readonly Action<WebDownload> ended;
    private readonly Func<string, bool> pathTaken;
    private CefBeforeDownloadCallback* asking;
    private string partialPath = string.Empty;
    private CefDownloadItemCallback* control;
    private string address = string.Empty;
    private string originalAddress = string.Empty;
    private string suggestedName = string.Empty;
    private string mimeType = string.Empty;
    private string filePath = string.Empty;
    private string pageAddress = string.Empty;
    private DateTimeOffset? startTime;
    private DateTimeOffset? endTime;
    private WebDownloadInterruption interruptReason;
    private long totalBytes = -1;
    private long receivedBytes;
    private long bytesPerSecond;
    private double? progress;
    private WebDownloadState state = WebDownloadState.Asked;

    internal WebDownload(uint id, Action<WebDownload> saved, Action<WebDownload> ended, Func<string, bool> pathTaken)
    {
        Id = id;
        this.saved = saved;
        this.ended = ended;
        this.pathTaken = pathTaken;
    }

    /// <summary>A download the profile kept from an earlier run: Chromium knows nothing of it, so it cannot go on. One
    /// that was coming in is listed as interrupted - the application closed meanwhile.</summary>
    internal static WebDownload Restore(WebViewKeptDownload kept, Action<WebDownload> ended)
    {
        var state = Enum.IsDefined(kept.State) ? kept.State : WebDownloadState.Interrupted;
        var comingIn = state is not (WebDownloadState.Completed or WebDownloadState.Cancelled);
        var reason = !comingIn ? WebDownloadInterruption.None
            : state == WebDownloadState.Interrupted && kept.InterruptReason != WebDownloadInterruption.None &&
              Enum.IsDefined(kept.InterruptReason)
                ? kept.InterruptReason
                : WebDownloadInterruption.ApplicationClosed;
        var total = kept.TotalBytes >= 0 ? kept.TotalBytes : -1;
        var received = Math.Max(0, kept.ReceivedBytes);
        var download = new WebDownload(0, null, ended, null)
        {
            address = kept.Address ?? string.Empty,
            originalAddress = kept.OriginalAddress ?? string.Empty,
            pageAddress = kept.PageAddress ?? string.Empty,
            suggestedName = kept.Name ?? string.Empty,
            mimeType = kept.MimeType ?? string.Empty,
            filePath = kept.FilePath ?? string.Empty,
            partialPath = comingIn ? kept.PartialPath ?? string.Empty : string.Empty,
            totalBytes = total,
            receivedBytes = received,
            progress = state == WebDownloadState.Completed ? 1
                : total > 0 ? Math.Clamp((double)received / total, 0, 1) : null,
            state = comingIn ? WebDownloadState.Interrupted : state,
            interruptReason = reason,
            startTime = kept.StartTime,
            endTime = state == WebDownloadState.Completed ? kept.EndTime : null,
            Ended = true
        };
        download.KeptAs = download.Kept();
        return download;
    }

    /// <summary>It as the profile's file keeps it.</summary>
    internal WebViewKeptDownload Kept() => new(address, originalAddress, pageAddress, suggestedName, mimeType, filePath,
        partialPath.Length > 0 && partialPath != filePath ? partialPath : null, totalBytes, receivedBytes, state,
        interruptReason, startTime, endTime);

    /// <summary>It as the profile's file was last written with; null while it is not listed.</summary>
    internal WebViewKeptDownload KeptAs { get; set; }

    /// <summary>Whether the profile's file says of it what is so no longer - its state or why it stopped.</summary>
    internal bool KeptDiffers => KeptAs != null && (KeptAs.State != state || KeptAs.InterruptReason != interruptReason);

    /// <summary>Chromium's number for it, unique in the profile while the application runs; 0 for one kept from an
    /// earlier run.</summary>
    internal uint Id { get; }

    /// <summary>The browser Chromium tells of it through - the page that started it, kept until it is done; let go of
    /// once it has ended.</summary>
    internal WebViewBrowser Origin { get; set; }

    /// <summary>Where it comes from, after any redirect.</summary>
    public string Address
    {
        get => address;
        private set => Set(ref address, value);
    }

    /// <summary>The address the page asked for, before any redirect.</summary>
    public string OriginalAddress
    {
        get => originalAddress;
        private set => Set(ref originalAddress, value);
    }

    /// <summary>The file name the site gives, or Chromium makes up from the address, as it was asked.</summary>
    public string SuggestedName
    {
        get => suggestedName;
        private set => Set(ref suggestedName, value);
    }

    /// <summary>Its type, as the server says - "application/pdf"; empty for none said.</summary>
    public string MimeType
    {
        get => mimeType;
        private set => Set(ref mimeType, value);
    }

    /// <summary>Where it is saved, as the application said; empty until it has. Chromium writes to a file beside it
    /// while it comes in, and names it so once it is complete.</summary>
    public string FilePath
    {
        get => filePath;
        private set => Set(ref filePath, value);
    }

    /// <summary>The page it was downloaded from, as it was when asked; empty when it was none - a page of its own.
    /// </summary>
    public string PageAddress
    {
        get => pageAddress;
        private set => Set(ref pageAddress, value);
    }

    /// <summary>When it started; null until Chromium says.</summary>
    public DateTimeOffset? StartTime
    {
        get => startTime;
        private set => Set(ref startTime, value);
    }

    /// <summary>When it completed; null until it has.</summary>
    public DateTimeOffset? EndTime
    {
        get => endTime;
        private set => Set(ref endTime, value);
    }

    /// <summary>Whether <see cref="Resume"/> can be asked: paused, or interrupted in this run - Chromium tries it again,
    /// from where it stopped when the server allows, else from the start, and it may stop again for the same reason.
    /// One kept from an earlier run cannot; it is downloaded again (<see cref="WebViewBridge.StartDownload"/>).</summary>
    public bool CanResume => control != null && state is WebDownloadState.Paused or WebDownloadState.Interrupted;

    /// <summary>Its size in bytes; -1 while the server has not said.</summary>
    public long TotalBytes
    {
        get => totalBytes;
        private set => Set(ref totalBytes, value);
    }

    /// <summary>The bytes come in so far.</summary>
    public long ReceivedBytes
    {
        get => receivedBytes;
        private set => Set(ref receivedBytes, value);
    }

    /// <summary>How fast it comes in, bytes a second.</summary>
    public long BytesPerSecond
    {
        get => bytesPerSecond;
        private set => Set(ref bytesPerSecond, value);
    }

    /// <summary>How far it has come, 0 to 1; null while its size is not known.</summary>
    public double? Progress
    {
        get => progress;
        private set => Set(ref progress, value);
    }

    /// <summary>Where it is.</summary>
    public WebDownloadState State
    {
        get => state;
        private set => Set(ref state, value);
    }

    /// <summary>What stopped it; <see cref="WebDownloadInterruption.None"/> unless <see cref="WebDownloadState.Interrupted"/>.
    /// </summary>
    public WebDownloadInterruption InterruptReason
    {
        get => interruptReason;
        private set => Set(ref interruptReason, value);
    }

    /// <summary>Whether it is done with - completed or cancelled. An interrupted one is not: it may go on.</summary>
    public bool IsFinished => state is WebDownloadState.Completed or WebDownloadState.Cancelled;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Stops it for good, the part come in thrown away; asked, it is refused. Nothing once it is finished.
    /// </summary>
    public void Cancel()
    {
        if (asking != null)
        {
            Refuse();
            ended?.Invoke(this);
            return;
        }

        if (state == WebDownloadState.Interrupted)
        {
            // Chromium cancels only what comes in: an interrupted download is given up here, its part with it.
            GiveUpInterrupted();
            return;
        }

        if (control != null && !IsFinished)
        {
            control->cancel(control);
        }
    }

    private void GiveUpInterrupted()
    {
        TakeControl(null);
        if (partialPath.Length > 0 && partialPath != filePath)
        {
            try
            {
                File.Delete(partialPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Serilog.Log.Logger.Warning(exception, "The part of a download given up could not be deleted: {Path}", partialPath);
            }
        }

        InterruptReason = WebDownloadInterruption.None;
        IsGivenUp = true;
        // Told before it counts as ended: an application forgetting it as it finishes leaves its page to be closed.
        State = WebDownloadState.Cancelled;
        Ended = true;
        ended?.Invoke(this);
    }

    /// <summary>Whether it was given up here, interrupted, while Chromium kept it to resume.</summary>
    internal bool IsGivenUp { get; private set; }

    private static WebDownloadInterruption Interruption(CefDownloadInterruptReason reason)
    {
        var name = reason.ToString();
        return name.StartsWith("File", StringComparison.Ordinal) ? WebDownloadInterruption.File
            : name.StartsWith("Network", StringComparison.Ordinal) ? WebDownloadInterruption.Network
            : name.StartsWith("Server", StringComparison.Ordinal) ? WebDownloadInterruption.Server
            : name.StartsWith("User", StringComparison.Ordinal) ? WebDownloadInterruption.User
            : name.StartsWith("Crash", StringComparison.Ordinal) ? WebDownloadInterruption.Crash
            : WebDownloadInterruption.Other;
    }

    /// <summary>Pauses it while it comes in.</summary>
    public void Pause()
    {
        if (control != null && state == WebDownloadState.InProgress)
        {
            control->pause(control);
        }
    }

    /// <summary>Goes on with it once paused, or interrupted.</summary>
    public void Resume()
    {
        if (control != null && state is WebDownloadState.Paused or WebDownloadState.Interrupted)
        {
            control->resume(control);
        }
    }

    /// <summary>Whether it is asked and not answered yet.</summary>
    internal bool IsAsked => asking != null;


    /// <summary>Whether Chromium says no more of it: it finished, it was given up here, or it was kept from an earlier
    /// run, which Chromium never heard of.</summary>
    internal bool Ended { get; private set; }

    /// <summary>Saves it at <paramref name="path"/>, a full path with the file's name; the folder is made if need be.
    /// Once only, while it is asked: false otherwise - answered before, refused, its page gone, or finished.</summary>
    internal bool Save(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is no full path to save at.", nameof(path));
        }

        if (asking == null || Ended)
        {
            return false;
        }

        var held = asking;
        asking = null;
        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, path);
            held->cont(held, &target, 0);
        }
        finally
        {
            CefStrings.Clear(&target);
            CefRefCounting.ReleaseCef(held);
        }

        FilePath = path;
        if (state == WebDownloadState.Asked)
        {
            State = WebDownloadState.InProgress;
        }

        saved?.Invoke(this);
        return true;
    }

    /// <summary>Answers Chromium asking again - as it resumes a download that was interrupted - with the path the
    /// application gave; false when it gave none yet.</summary>
    internal bool GoOnWhereSaved(CefBeforeDownloadCallback* callback)
    {
        if (filePath.Length == 0)
        {
            return false;
        }

        var target = new CefStringUtf16();
        try
        {
            CefStrings.Set(&target, filePath);
            callback->cont(callback, &target, 0);
        }
        finally
        {
            CefStrings.Clear(&target);
        }

        return true;
    }

    /// <summary>Refuses it while it is asked: Chromium cancels it, nothing saved.</summary>
    internal void Refuse()
    {
        if (asking == null)
        {
            return;
        }

        LetGoOfAsking();
        State = WebDownloadState.Cancelled;
    }

    /// <summary>Holds Chromium's question - where to save it - until the application answers.</summary>
    internal void Ask(CefBeforeDownloadCallback* callback)
    {
        LetGoOfAsking();
        CefRefCounting.AddRefCef(callback);
        asking = callback;
        State = WebDownloadState.Asked;
    }

    /// <summary>Holds the means to cancel, pause and go on - the latest Chromium gave - while it has not ended.
    /// </summary>
    internal void TakeControl(CefDownloadItemCallback* callback)
    {
        var couldResume = CanResume;
        Hold(callback);
        if (couldResume != CanResume)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanResume)));
        }
    }

    private void Hold(CefDownloadItemCallback* callback)
    {
        if (control != null)
        {
            CefRefCounting.ReleaseCef(control);
            control = null;
        }

        if (callback != null && !Ended)
        {
            CefRefCounting.AddRefCef(callback);
            control = callback;
        }
    }

    /// <summary>Takes what Chromium says of it now; borrowed, not kept.</summary>
    internal void Read(CefDownloadItem* item)
    {
        Address = CefStrings.ReadAndFree(item->get_url(item));
        OriginalAddress = CefStrings.ReadAndFree(item->get_original_url(item));
        var name = CefStrings.ReadAndFree(item->get_suggested_file_name(item));
        if (suggestedName.Length == 0)
        {
            Suggest(name);
        }
        MimeType = CefStrings.ReadAndFree(item->get_mime_type(item));
        if (Ended)
        {
            // Given up here: what Chromium still says of it changes nothing.
            return;
        }

        var current = CefStrings.ReadAndFree(item->get_full_path(item));
        if (current.Length > 0 && item->is_complete(item) == 0)
        {
            partialPath = current;
        }

        StartTime = Time(item->get_start_time(item));
        // Chromium dates a cancel and an interruption too: only completing is told as the end.
        EndTime = item->is_complete(item) != 0 ? Time(item->get_end_time(item)) ?? DateTimeOffset.UtcNow : null;

        var total = item->get_total_bytes(item);
        ReceivedBytes = item->get_received_bytes(item);
        BytesPerSecond = item->get_current_speed(item);
        var percent = item->get_percent_complete(item);
        if (item->is_complete(item) != 0)
        {
            TotalBytes = ReceivedBytes;
            Progress = 1;
            InterruptReason = WebDownloadInterruption.None;
            if (state != WebDownloadState.Completed)
            {
                MarkFromTheInternet();
            }

            State = WebDownloadState.Completed;
        }
        else
        {
            TotalBytes = total > 0 ? total : -1;
            Progress = percent >= 0 ? percent / 100.0 : null;
            if (item->is_canceled(item) != 0)
            {
                State = WebDownloadState.Cancelled;
            }
            else if (item->is_interrupted(item) != 0)
            {
                InterruptReason = Interruption(item->get_interrupt_reason(item));
                State = WebDownloadState.Interrupted;
            }
            else if (asking == null && FilePath.Length > 0)
            {
                InterruptReason = WebDownloadInterruption.None;
                State = item->is_paused(item) != 0 ? WebDownloadState.Paused : WebDownloadState.InProgress;
            }
        }

        if (IsFinished)
        {
            // Finished before the application answered: there is nothing left to answer.
            LetGoOfAsking();
            Ended = true;
            TakeControl(null);
        }
    }

    private static DateTimeOffset? Time(CefBasetime time) =>
        time.val > 0 && time.val < DateTime.MaxValue.ToFileTimeUtc() / 10
            ? new DateTimeOffset(DateTime.FromFileTimeUtc(time.val * 10))
            : null;

    /// <summary>Takes the page it is downloaded from, the first time it is told.</summary>
    internal void SeenOn(string page)
    {
        // A page opened only to download - a link to a file opened as a new page - is no page it came from.
        if (pageAddress.Length == 0 && !string.IsNullOrEmpty(page) && page != WebViewBrowser.Blank && page != address &&
            page != originalAddress)
        {
            PageAddress = page;
        }
    }

    /// <summary>A full path in <paramref name="folder"/> for it, under its suggested name made fit for a file - no
    /// folder in it, no name the system keeps for a device, no longer than a name can be - numbered when a file has that
    /// name already or another download is saved there.</summary>
    internal string FreePathIn(string folder)
    {
        var name = Path.GetFileName(suggestedName ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            name = "download";
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        // What comes before the first dot is what Windows takes for a device, spaces before the dot aside: NUL.tar.gz too.
        if (OperatingSystem.IsWindows() && IsDeviceName(name.Split('.')[0].TrimEnd(' ')))
        {
            stem = "_" + stem;
        }

        if (extension.Length > NameLimit / 2)
        {
            extension = extension[..(NameLimit / 2)];
        }

        if (stem.Length + extension.Length > NameLimit)
        {
            stem = stem[..(NameLimit - extension.Length)].TrimEnd('.', ' ');
        }

        name = stem + extension;
        var path = Path.Combine(folder, name);
        for (var number = 1; IsTaken(path); number++)
        {
            path = Path.Combine(folder, $"{stem} ({number}){extension}");
        }

        return path;
    }

    // On Windows a file downloaded is marked as from the internet, as browsers mark it - the zone Explorer, SmartScreen
    // and Office ask before they open or run it - unless Chromium marked it already.
    private void MarkFromTheInternet()
    {
        var zone = filePath + ":Zone.Identifier";
        if (!OperatingSystem.IsWindows() || filePath.Length == 0 ||
            !Uri.TryCreate(address, UriKind.Absolute, out var source) || source.Scheme == WebViewSchemes.App ||
            File.Exists(zone))
        {
            return;
        }

        var host = source.Scheme is "http" or "https" or "ftp" ? address : "about:internet";
        var referrer = Uri.TryCreate(pageAddress, UriKind.Absolute, out var page) && page.Scheme is "http" or "https"
            ? $"ReferrerUrl={pageAddress}\r\n"
            : string.Empty;
        try
        {
            File.WriteAllText(zone, $"[ZoneTransfer]\r\nZoneId=3\r\n{referrer}HostUrl={host}\r\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Serilog.Log.Logger.Warning(exception, "A download could not be marked as from the internet: {Path}", filePath);
        }
    }

    private bool IsTaken(string path) =>
        File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".crdownload") || pathTaken?.Invoke(path) == true;

    private static bool IsDeviceName(string beforeDot)
    {
        var upper = beforeDot.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
               (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) ||
                                      upper.StartsWith("LPT", StringComparison.Ordinal)) &&
                upper[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }

    /// <summary>Takes the name Chromium suggests as it asks; later updates keep it.</summary>
    internal void Suggest(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            SuggestedName = name;
        }
    }

    /// <summary>Lets go of all Chromium gave, before it shuts down: one still asked is refused.</summary>
    internal void LetGo()
    {
        Refuse();
        TakeControl(null);
    }

    private void LetGoOfAsking()
    {
        if (asking == null)
        {
            return;
        }

        var held = asking;
        asking = null;
        // A callback let go of unrun cancels the download.
        CefRefCounting.ReleaseCef(held);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        var wasFinished = IsFinished;
        var couldResume = CanResume;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(State) && wasFinished != IsFinished)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFinished)));
        }

        if (name == nameof(State) && couldResume != CanResume)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanResume)));
        }
    }
}
