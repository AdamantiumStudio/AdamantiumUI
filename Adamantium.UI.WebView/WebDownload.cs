using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>
/// A file a page downloads: what it is, how far it has come, and the means to pause, go on or cancel. Asked of the
/// application first (<see cref="WebViewBridge.DownloadRequested"/>), which says where to save it; once saved there it
/// is among <see cref="WebViewEngine.Downloads"/>. Its properties change as it comes in; it is used on the UI thread.
/// </summary>
public sealed unsafe class WebDownload : INotifyPropertyChanged
{
    private readonly Action<WebDownload> saved;
    private readonly Action<WebDownload> ended;
    private CefBeforeDownloadCallback* asking;
    private string partialPath = string.Empty;
    private CefDownloadItemCallback* control;
    private string address = string.Empty;
    private string originalAddress = string.Empty;
    private string suggestedName = string.Empty;
    private string mimeType = string.Empty;
    private string filePath = string.Empty;
    private string interruptReason = string.Empty;
    private long totalBytes = -1;
    private long receivedBytes;
    private long bytesPerSecond;
    private double? progress;
    private WebDownloadState state = WebDownloadState.Asked;

    internal WebDownload(uint id, Action<WebDownload> saved, Action<WebDownload> ended)
    {
        Id = id;
        this.saved = saved;
        this.ended = ended;
    }

    /// <summary>Chromium's number for it, unique in the profile while the application runs.</summary>
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

    /// <summary>What stopped it, as Chromium names it - "NetworkFailed", "FileNoSpace"; empty unless
    /// <see cref="WebDownloadState.Interrupted"/>.</summary>
    public string InterruptReason
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
        Ended = true;
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

        InterruptReason = string.Empty;
        State = WebDownloadState.Cancelled;
        ended?.Invoke(this);
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


    /// <summary>Whether Chromium has said it is finished: it says no more of it.</summary>
    internal bool Ended { get; private set; }

    /// <summary>Saves it at <paramref name="path"/>, a full path with the file's name; the folder is made if need be.
    /// Once only, while it is asked: nothing otherwise.</summary>
    internal void Save(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is no full path to save at.", nameof(path));
        }

        if (asking == null)
        {
            return;
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
        State = WebDownloadState.InProgress;
        saved?.Invoke(this);
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

        var total = item->get_total_bytes(item);
        ReceivedBytes = item->get_received_bytes(item);
        BytesPerSecond = item->get_current_speed(item);
        var percent = item->get_percent_complete(item);
        if (item->is_complete(item) != 0)
        {
            TotalBytes = ReceivedBytes;
            Progress = 1;
            InterruptReason = string.Empty;
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
                InterruptReason = item->get_interrupt_reason(item).ToString();
                State = WebDownloadState.Interrupted;
            }
            else if (asking == null && FilePath.Length > 0)
            {
                InterruptReason = string.Empty;
                State = item->is_paused(item) != 0 ? WebDownloadState.Paused : WebDownloadState.InProgress;
            }
        }

        if (IsFinished)
        {
            Ended = true;
            TakeControl(null);
        }
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
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(State) && wasFinished != IsFinished)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFinished)));
        }
    }
}
