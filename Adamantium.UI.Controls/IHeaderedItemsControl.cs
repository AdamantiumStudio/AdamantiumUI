using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>An <see cref="ItemsControl"/> that also carries a header (label) - the container a menu, a tree or a ribbon
/// tab makes for each of its items: the item is the header, drawn by the owner's item template, and a
/// <see cref="HierarchicalDataTemplate"/> supplies the children as well. Implemented by <see cref="Primitives.MenuItem"/>,
/// <see cref="TreeViewItem"/>, <see cref="RibbonTab"/> and <see cref="RibbonGroup"/>.</summary>
public interface IHeaderedItemsControl
{
    object Header { get; set; }

    /// <summary>Template that renders <see cref="Header"/>. The owner sets this to its item template, so the header of
    /// every generated container is drawn the same way.</summary>
    DataTemplate HeaderTemplate { get; set; }
}
