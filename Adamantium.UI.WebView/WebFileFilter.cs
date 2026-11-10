namespace Adamantium.UI.WebView;

/// <summary>A kind of file a page's file field takes - its <c>accept</c>: what a person reads, and the extensions, bare,
/// without the dot; none when Chromium knows none for it.</summary>
public sealed class WebFileFilter
{
    public WebFileFilter(string name, IReadOnlyList<string> extensions)
    {
        Name = name;
        Extensions = extensions ?? [];
    }

    /// <summary>What a person reads: "Image Files", or the type as the page wrote it.</summary>
    public string Name { get; }

    /// <summary>The extensions, bare - "png", "jpg".</summary>
    public IReadOnlyList<string> Extensions { get; }
}
