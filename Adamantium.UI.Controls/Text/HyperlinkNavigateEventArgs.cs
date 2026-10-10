using System;
using System.ComponentModel;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Hyperlink"/> asks to go to <see cref="Uri"/>; a handler that goes there itself sets
/// <see cref="HandledEventArgs.Handled"/>.</summary>
public class HyperlinkNavigateEventArgs : HandledEventArgs
{
    public HyperlinkNavigateEventArgs(Uri uri)
    {
        Uri = uri;
    }

    /// <summary>Where the link goes.</summary>
    public Uri Uri { get; }
}
