namespace Adamantium.UI.Core.Automation;

/// <summary>An item inside something that scrolls, such as a row of a list.</summary>
public interface IScrollItemProvider
{
    /// <summary>Scrolls what holds the item until it is in view, making its element first if it had none.</summary>
    void ScrollIntoView();
}
