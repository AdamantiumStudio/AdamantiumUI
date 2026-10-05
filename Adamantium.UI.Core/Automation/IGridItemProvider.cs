namespace Adamantium.UI.Core.Automation;

/// <summary>A cell of an <see cref="IGridProvider"/>.</summary>
public interface IGridItemProvider
{
    int Row { get; }

    int Column { get; }

    /// <summary>The peer of the grid that holds the cell.</summary>
    AutomationPeer ContainingGrid { get; }
}
