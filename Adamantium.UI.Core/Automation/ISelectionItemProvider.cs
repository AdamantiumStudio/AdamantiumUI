namespace Adamantium.UI.Core.Automation;

/// <summary>One item of an <see cref="ISelectionProvider"/>, such as a tab.</summary>
public interface ISelectionItemProvider
{
    bool IsSelected { get; }

    /// <summary>The peer of the element that holds this item.</summary>
    AutomationPeer SelectionContainer { get; }

    /// <summary>Selects this item, the way a click does.</summary>
    void Select();
}
