namespace Adamantium.UI.Controls;

/// <summary>Where pinned tabs live: <see cref="SeparateRow"/>, their own wrapping row above the others (as in Rider), or
/// <see cref="SameRow"/>, first in the one row (as in Visual Studio).</summary>
public enum PinnedTabsPlacement
{
    SeparateRow,
    SameRow
}
