namespace Adamantium.UI.Core.Automation;

/// <summary>A grid whose columns have headers.</summary>
public interface ITableProvider
{
    IReadOnlyList<AutomationPeer> GetColumnHeaders();
}
