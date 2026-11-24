namespace Adamantium.UI.Core.Printing;

/// <summary>What a <see cref="PageRange"/> picks.</summary>
public enum PageRangeKind
{
    /// <summary>Every page.</summary>
    All,

    /// <summary>The page shown when printing was asked for.</summary>
    Current,

    /// <summary>The pages written out.</summary>
    Chosen,
}
