namespace Adamantium.UI.Core.Automation;

/// <summary>A cell of an <see cref="ITableProvider"/>, which knows the headers over it.</summary>
public interface ITableItemProvider
{
    /// <summary>The peers of the headers of the item's row.</summary>
    IReadOnlyList<AutomationPeer> GetRowHeaders();

    /// <summary>The peers of the headers of the item's columns.</summary>
    IReadOnlyList<AutomationPeer> GetColumnHeaders();
}
