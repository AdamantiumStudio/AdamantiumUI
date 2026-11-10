namespace Adamantium.UI.WebView;

/// <summary>A site of the application's whose files are a folder's: a path names a file under it, a folder's path its
/// <c>index.html</c>. A path that would leave the folder names nothing.</summary>
public sealed class WebSiteFolder : IWebSiteContent
{
    private readonly string root;

    /// <summary>A site of the files in <paramref name="folder"/>.</summary>
    public WebSiteFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
    }

    public Stream Open(string path)
    {
        if (path == null || path.Contains('\0'))
        {
            return null;
        }

        var file = Path.GetFullPath(Path.Combine(root, path.Replace('\\', '/').TrimStart('/')));
        if (!file.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return null;
        }

        if (Directory.Exists(file))
        {
            file = Path.Combine(file, "index.html");
        }

        return File.Exists(file) ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }
}
