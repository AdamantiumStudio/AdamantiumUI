namespace Adamantium.UI.Controls.DataGrid;

/// <summary>The panel a row opens under itself, from the page's template. A node of the rows' tree, so virtualization handles
/// it; opened independently of the row's branch.</summary>
public sealed class DataGridRowDetails
{
    internal DataGridRowDetails(object item)
    {
        Item = item;
    }

    /// <summary>The record this panel is the long form of - what its template binds against.</summary>
    public object Item { get; }
}
