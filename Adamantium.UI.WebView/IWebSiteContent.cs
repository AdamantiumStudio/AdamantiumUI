namespace Adamantium.UI.WebView;

/// <summary>
/// The files of a site of the application's, served at <c>app://site/path</c> once added with
/// <see cref="WebViewEngine.AddSite"/>: <see cref="WebSiteFolder"/> serves a folder, <see cref="WebSiteResources"/> the
/// resources built into an assembly; anything else - an archive, files in memory - is an implementation of its own.
/// Asked on CEF's own threads, at once by several requests.
/// </summary>
public interface IWebSiteContent
{
    /// <summary>The file at <paramref name="path"/> - the address's path without its first slash, its escapes undone,
    /// empty for the site's root, ending in a slash for a folder - to read from its start; null for none, which answers
    /// 404. The caller disposes it. Its type is told by its extension. A folder asked for without its last slash, which
    /// the path with one opens, is sent there.</summary>
    Stream Open(string path);
}
