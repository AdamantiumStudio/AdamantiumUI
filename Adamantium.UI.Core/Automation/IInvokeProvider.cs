namespace Adamantium.UI.Core.Automation;

/// <summary>An element that does one thing when pressed, such as a button.</summary>
public interface IInvokeProvider
{
    /// <summary>Does what a click does.</summary>
    void Invoke();
}
