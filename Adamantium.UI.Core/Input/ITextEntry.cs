namespace Adamantium.UI.Core.Input;

/// <summary>An element that types the keys it is given. While it has the focus and takes typing, a key that would type
/// stays with it: a key binding elsewhere in the window takes only a key held with Ctrl, Alt or Meta, or a function key,
/// and what holds the element takes no key that types for a gesture of its own.</summary>
public interface ITextEntry
{
    /// <summary>Whether keys typed now become text - false for a read-only entry, whose keys go on as anyone else's.
    /// </summary>
    bool TakesTyping { get; }
}
