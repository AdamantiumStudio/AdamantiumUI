using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// A page's file field asks for files - or a folder, or where to save. The application asks the user and answers with
/// <see cref="Choose"/>, or <see cref="Cancel"/>; it says it will with <see cref="Handled"/>, and may answer later.
/// Unhandled, the page gets nothing, as when the user cancels. Raised on the UI thread, outside the browser's own work,
/// so a modal dialog may be shown from it.
/// </summary>
public sealed class WebFileDialogRequestedEventArgs : EventArgs
{
    private readonly WebViewFileDialogRequest request;

    internal WebFileDialogRequestedEventArgs(WebViewFileDialogRequest request)
    {
        this.request = request;
    }

    /// <summary>What is asked for.</summary>
    public WebFileDialogMode Mode => request.Mode;

    /// <summary>The caption the page gives; empty for none.</summary>
    public string Title => request.Title;

    /// <summary>The file or folder to start at; empty for none.</summary>
    public string DefaultPath => request.DefaultPath;

    /// <summary>The kinds of file the field takes; empty for any.</summary>
    public IReadOnlyList<WebFileFilter> Filters => request.Filters;

    /// <summary>Set when the application answers, now or later.</summary>
    public bool Handled { get; set; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered => request.IsAnswered;

    /// <summary>Gives the page <paramref name="paths"/> - full paths; none is a cancel.</summary>
    public void Choose(IReadOnlyList<string> paths) => request.Choose(paths);

    /// <summary>Gives the page nothing.</summary>
    public void Cancel() => request.Cancel();

    /// <summary>Asks the user through the platform's own dialog (<see cref="FileDialog"/>) - the kinds the field takes
    /// on offer - and gives the page what was chosen; nothing when the platform has no dialog or the user cancels. One
    /// file even when the field takes several: the platform's dialog chooses one. Over <paramref name="owner"/>, the
    /// window the page is in; unset, the foreground one.</summary>
    public void AnswerFromFileDialog(IntPtr owner = default)
    {
        Handled = true;
        var types = Filters.Where(filter => filter.Extensions.Count > 0)
            .Select(filter => new FileType(filter.Name, filter.Extensions.ToArray()))
            .ToArray();
        var title = string.IsNullOrEmpty(Title) ? null : Title;
        var chosen = Mode switch
        {
            WebFileDialogMode.Save => FileDialog.Save(new SaveFileRequest
            {
                Title = title,
                FileName = string.IsNullOrEmpty(DefaultPath) ? null : Path.GetFileName(DefaultPath),
                FileTypes = types,
                Key = "webview.save",
                Owner = owner
            }),
            WebFileDialogMode.OpenFolder => FileDialog.OpenFolder(new OpenFolderRequest
            {
                Title = title,
                Key = "webview.folder",
                Owner = owner
            }),
            _ => FileDialog.Open(new OpenFileRequest { Title = title, FileTypes = types, Key = "webview.open", Owner = owner })
        };
        if (chosen == null)
        {
            Cancel();
            return;
        }

        Choose([chosen]);
    }
}
