using System;
using System.Collections.Generic;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

// Drives self-or-descendant states (IsMouseOver, IsKeyboardFocusWithin): when the focal leaf moves, raises leave/enter
// only on elements that left or joined its ancestor chain.
internal static class AncestorState
{
    /// <param name="makeArgs">Creates a fresh event-args for the given event - one per raise, so per-element Handled /
    /// Source state never leaks between targets.</param>
    public static void Transition(IInputComponent oldLeaf, IInputComponent newLeaf,
        RoutedEvent enterEvent, RoutedEvent leaveEvent, Func<RoutedEvent, RoutedEventArgs> makeArgs)
    {
        if (ReferenceEquals(oldLeaf, newLeaf)) return;

        var oldChain = AncestorChain(oldLeaf);
        var newChain = AncestorChain(newLeaf);
        var oldSet = new HashSet<IInputComponent>(oldChain);
        var newSet = new HashSet<IInputComponent>(newChain);

        foreach (var element in oldChain)   // left the chain (deepest first)
        {
            if (!newSet.Contains(element))
                element.RaiseEvent(makeArgs(leaveEvent));
        }

        foreach (var element in newChain)   // joined the chain (deepest first)
        {
            if (!oldSet.Contains(element))
                element.RaiseEvent(makeArgs(enterEvent));
        }
    }

    /// <summary>Visual ancestor chain (deepest first) of the input-aware components from <paramref name="leaf"/> up to
    /// the root; a null leaf yields an empty chain.</summary>
    public static List<IInputComponent> AncestorChain(IInputComponent leaf)
    {
        var chain = new List<IInputComponent>();
        IUIComponent v = leaf;
        while (v != null)
        {
            if (v is IInputComponent input)
                chain.Add(input);
            v = v.VisualParent;
        }
        return chain;
    }
}
