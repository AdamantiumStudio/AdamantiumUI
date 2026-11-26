using System.Collections.Generic;
using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>A font of a page: its resource in the package and its part's name, the glyphs the page uses, and the runs set
/// in it.</summary>
internal sealed class XpsPageFont(IXpsOMFontResource resource, object part)
{
    public IXpsOMFontResource Resource { get; } = resource;

    public object Part { get; } = part;

    public HashSet<uint> Used { get; } = [];

    public List<XpsGlyphRun> Runs { get; } = [];
}
