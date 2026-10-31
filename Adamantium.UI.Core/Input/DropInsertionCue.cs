namespace Adamantium.UI.Core.Input;

/// <summary>How a list under a drag shows where in it the drop lands.</summary>
public enum DropInsertionCue
{
    /// <summary>A real gap opens at the insertion point where the list's panel can open one; elsewhere a caret marks
    /// it.</summary>
    Gap,

    /// <summary>A caret marks the insertion point - for a list whose items are expensive to re-arrange, or where the
    /// quieter cue is wanted.</summary>
    Caret,

    /// <summary>Nothing: the list keeps an order of its own - one sorted by name, say - and a drop lands in it as a
    /// whole, with no insertion point or tree placement.</summary>
    None
}
