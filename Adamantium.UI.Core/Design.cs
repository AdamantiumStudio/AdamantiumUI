namespace Adamantium.UI.Core;

/// <summary>
/// Design-time flag, mirroring WPF's <c>DesignerProperties.GetIsInDesignMode</c> and Avalonia's
/// <c>Design.IsDesignMode</c>. The AUML designer host sets this before loading markup so design-unsafe code -
/// e.g. a behavior that spins up a universe - can opt out and stay dormant in the previewer.
/// </summary>
public static class Design
{
    /// <summary>True while markup is being loaded for the designer/previewer rather than the running app.</summary>
    public static bool IsDesignMode { get; set; }

    /// <summary>
    /// True while the designer is rendering a LIVE preview (a continuously-ticked frame stream) rather than a single
    /// static shot. Animations play in design mode only when this is set, so a one-shot render still captures the
    /// settled state while the live previewer animates.
    /// </summary>
    public static bool IsLivePreview { get; set; }

    // Weak: a source must never keep a discarded preview alive.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, DesignSource> Sources = new();

    /// <summary>Records where generated code built <paramref name="element"/>, so the designer can take a click on an element
    /// of a nested view back to that view's markup. Does nothing outside the designer.</summary>
    public static void Source(object element, string file, int line, int column)
    {
        if (!IsDesignMode || element == null) return;
        Sources.AddOrUpdate(element, new DesignSource(file, line, column));
    }

    /// <summary>Where generated code built <paramref name="element"/>; null when nothing recorded it.</summary>
    public static DesignSource SourceOf(object element) =>
        element != null && Sources.TryGetValue(element, out var source) ? source : null;
}

/// <summary>A place in a markup file: its full path, line and column (1-based, as the parser counts them).</summary>
public sealed record DesignSource(string File, int Line, int Column);
