namespace Adamantium.UI.Core.Automation;

/// <summary>An element that opens and closes what it holds: a drop-down, a submenu, a tree branch.</summary>
public interface IExpandCollapseProvider
{
    ExpandCollapseState ExpandCollapseState { get; }

    void Expand();

    void Collapse();
}
