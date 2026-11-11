using Adamantium.UI.Core;

namespace Adamantium.UI.WebView;

/// <summary>
/// A page downloads a file - a link to one, a response the server sends as an attachment. The application says where
/// to save it with <see cref="Save"/>, or refuses it with <see cref="Refuse"/>; it says it will with
/// <see cref="Handled"/>, and may answer later. Unhandled, the download is refused. Raised on the UI thread, outside
/// the browser's own work, so a modal dialog may be shown from it.
/// </summary>
public sealed class WebDownloadRequestedEventArgs : EventArgs
{
    internal WebDownloadRequestedEventArgs(WebDownload download)
    {
        Download = download;
    }

    /// <summary>The download asked about: its address, suggested name, type and size.</summary>
    public WebDownload Download { get; }

    /// <summary>Set when the application answers, now or later.</summary>
    public bool Handled { get; set; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered => !Download.IsAsked;

    /// <summary>Saves it at <paramref name="path"/> - a full path with the file's name; the folder is made if need be.
    /// It is then among <see cref="WebViewEngine.Downloads"/>.</summary>
    public void Save(string path) => Download.Save(path);

    /// <summary>Refuses it: nothing is downloaded.</summary>
    public void Refuse() => Download.Refuse();

    /// <summary>Asks the user where to save it through the platform's own dialog (<see cref="FileDialog"/>), its
    /// suggested name offered, and saves it there; refused when the platform has no dialog or the user cancels. Over
    /// <paramref name="owner"/>, the window the page is in; unset, the foreground one.</summary>
    public void SaveFromFileDialog(IntPtr owner = default)
    {
        Handled = true;
        var name = Download.SuggestedName;
        var extension = Path.GetExtension(name).TrimStart('.');
        var chosen = FileDialog.Save(new SaveFileRequest
        {
            FileName = string.IsNullOrEmpty(name) ? null : name,
            FileTypes = extension.Length > 0 ? [new FileType(extension.ToUpperInvariant(), [extension])] : [],
            Key = "webview.download",
            Owner = owner
        });
        if (chosen == null)
        {
            Refuse();
            return;
        }

        Save(chosen);
    }
}
