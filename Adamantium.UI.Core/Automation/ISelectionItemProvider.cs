using System;

namespace Adamantium.UI.Core.Automation;

/// <summary>One item of an <see cref="ISelectionProvider"/>, such as a tab.</summary>
public interface ISelectionItemProvider
{
    bool IsSelected { get; }

    /// <summary>The peer of the element that holds this item.</summary>
    AutomationPeer SelectionContainer { get; }

    /// <summary>Makes this item the selected one: whatever else was selected is not any more.</summary>
    void Select();

    /// <summary>Adds this item to what is selected, leaving the rest. By default, for a container that holds one selection
    /// at a time: nothing to do when it is the one, refused otherwise.</summary>
    void AddToSelection() => AddToOnlyOne(this);

    /// <summary>Takes this item out of what is selected, leaving the rest. By default, for a container that always holds
    /// its one selection: nothing to do when it is not selected, refused otherwise.</summary>
    void RemoveFromSelection() => RemoveFromOnlyOne(this);

    /// <summary>Adding to a selection of one: nothing to do for the item that is it, refused for any other.</summary>
    static void AddToOnlyOne(ISelectionItemProvider item)
    {
        if (!item.IsSelected)
        {
            throw new InvalidOperationException("Only one item is selected here at a time; select it instead.");
        }
    }

    /// <summary>Taking out of a selection that is always there: nothing to do for an item outside it, refused for the one
    /// in it.</summary>
    static void RemoveFromOnlyOne(ISelectionItemProvider item)
    {
        if (item.IsSelected)
        {
            throw new InvalidOperationException("Something is always selected here; select another item instead.");
        }
    }
}
