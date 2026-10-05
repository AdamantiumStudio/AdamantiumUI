namespace Adamantium.UI.Core.Automation;

/// <summary>An element laid out in rows and columns, such as a data grid.</summary>
public interface IGridProvider
{
    int RowCount { get; }

    int ColumnCount { get; }

    /// <summary>The cell at <paramref name="row"/> and <paramref name="column"/>, or null while it has no element.</summary>
    AutomationPeer GetItem(int row, int column);
}
