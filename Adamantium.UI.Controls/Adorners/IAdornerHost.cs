namespace Adamantium.UI.Controls.Adorners;

/// <summary>A window that owns an <see cref="AdornerLayer"/>, so tooling drives the overlay without casting to a concrete window.
/// It is not on <c>IWindow</c> because Core does not reference Controls.</summary>
public interface IAdornerHost
{
    AdornerLayer AdornerLayer { get; }
}
