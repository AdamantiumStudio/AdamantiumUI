namespace Adamantium.UI.Core.Automation;

/// <summary>An element whose items can be selected, such as a tab control or a list.</summary>
public interface ISelectionProvider
{
    bool CanSelectMultiple { get; }

    /// <summary>Whether one item is always selected, as one tab always is.</summary>
    bool IsSelectionRequired { get; }

    /// <summary>The peers of the selected items.</summary>
    IReadOnlyList<AutomationPeer> GetSelection();
}
