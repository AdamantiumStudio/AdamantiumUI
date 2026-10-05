namespace Adamantium.UI.Automation;

/// <summary>Something that went wrong quietly: a broken binding, a value set by a name its element has no property for,
/// an error written to the log.</summary>
public sealed class ErrorEntry
{
    /// <summary>Its place in the journal; a mark is the sequence of the last entry at the time.</summary>
    public long Sequence { get; set; }

    /// <summary>Binding, Property or Log.</summary>
    public string Kind { get; set; }

    public string Message { get; set; }
}
