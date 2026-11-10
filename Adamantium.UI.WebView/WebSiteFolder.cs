namespace Adamantium.UI.WebView;

/// <summary>
/// A site of the application's whose files are a folder's: a path names a file under it, a folder's path - with its
/// last slash - its <c>index.html</c>. A path that would leave the folder names nothing: nor does one through a link or
/// a junction, to a file's other stream, or to a name Windows would read as another (a last dot or space).
/// </summary>
public sealed class WebSiteFolder : IWebSiteContent
{
    private readonly string root;

    /// <summary>A site of the files in <paramref name="folder"/>.</summary>
    public WebSiteFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var full = Path.GetFullPath(folder);
        root = Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
    }

    public Stream Open(string path)
    {
        if (path == null || path.Contains('\0'))
        {
            return null;
        }

        path = path.Replace('\\', '/').TrimStart('/');
        var isFolder = path.Length == 0 || path.EndsWith('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Contains(':') || segment.EndsWith('.') && segment is not ("." or "..") || segment.EndsWith(' '))
            {
                return null;
            }
        }

        var file = Path.GetFullPath(Path.Combine(root, path));
        if (!file.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return null;
        }

        if (isFolder)
        {
            file = Path.Combine(file, "index.html");
        }

        return File.Exists(file) && !ThroughALink(file) ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    // Whether the file, or a folder on the way to it from the root, is a link or junction - which could lead out.
    private bool ThroughALink(string file)
    {
        for (var at = file; at.Length > root.Length; at = Path.GetDirectoryName(at))
        {
            if ((File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
