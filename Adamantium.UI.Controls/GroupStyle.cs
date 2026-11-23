using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>How an <see cref="ItemsControl"/> shows a level of its source's groups: the header each group gets, a row of
/// its own before the group's items. One per level of grouping, the first the outermost; a level past the last takes
/// the last.</summary>
public sealed class GroupStyle
{
    /// <summary>The header's template; its DataContext is the group - its <c>Name</c>, <c>ItemCount</c>, <c>Items</c>.
    /// Null shows the group's name as text.</summary>
    public DataTemplate HeaderTemplate { get; set; }

    /// <summary>Whether a click on a header of this level folds its group to the header, and opens it again.</summary>
    public bool CanCollapse { get; set; }
}
