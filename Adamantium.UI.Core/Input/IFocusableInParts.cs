namespace Adamantium.UI.Core.Input;

/// <summary>An element the keyboard can only reach in parts of it - a TextBlock's links: a press anywhere else in it
/// leaves the focus to the element around it, as if it could not take the keyboard at all.</summary>
public interface IFocusableInParts
{
    /// <summary>Whether the press lands on a part that takes the keyboard.</summary>
    bool TakesFocusAt(MouseButtonEventArgs e);
}
