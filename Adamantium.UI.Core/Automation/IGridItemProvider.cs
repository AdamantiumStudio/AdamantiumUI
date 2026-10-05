namespace Adamantium.UI.Core.Automation;

/// <summary>A cell of an <see cref="IGridProvider"/>.</summary>
public interface IGridItemProvider
{
    int Row { get; }

    int Column { get; }

    /// <summary>How many rows the item takes, from <see cref="Row"/>.</summary>
    int RowSpan => 1;

    /// <summary>How many columns the item takes, from <see cref="Column"/>: all of them for a whole row.</summary>
    int ColumnSpan => 1;

    /// <summary>The peer of the grid that holds the cell.</summary>
    AutomationPeer ContainingGrid { get; }
}
