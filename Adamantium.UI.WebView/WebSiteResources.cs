using System.Reflection;

namespace Adamantium.UI.WebView;

/// <summary>
/// A site of the application's whose files are resources built into an assembly: the file at <c>css/site.css</c> is
/// the resource named <c>prefix/css/site.css</c> - give each its name with <c>LogicalName</c>, as the build would make
/// another of it. A folder's path names its <c>index.html</c>.
/// </summary>
public sealed class WebSiteResources : IWebSiteContent
{
    private readonly Assembly assembly;
    private readonly string prefix;

    /// <summary>A site of the resources of <paramref name="assembly"/> whose names start with
    /// <paramref name="prefix"/> and a slash.</summary>
    public WebSiteResources(Assembly assembly, string prefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        this.assembly = assembly;
        this.prefix = prefix.TrimEnd('/') + "/";
    }

    public Stream Open(string path)
    {
        if (path == null)
        {
            return null;
        }

        path = path.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || path.EndsWith('/'))
        {
            path += "index.html";
        }

        return assembly.GetManifestResourceStream(prefix + path) ??
               assembly.GetManifestResourceStream(prefix + path + "/index.html");
    }
}
