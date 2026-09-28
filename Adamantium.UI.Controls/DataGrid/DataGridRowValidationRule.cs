using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>Whether a whole record is acceptable, for rules no single cell can answer (from before to, parts adding up).
/// Returns a message; null or empty means valid.</summary>
public abstract class DataGridRowValidationRule : FundamentalUIComponent
{
    /// <summary>What is wrong with <paramref name="item"/> taken as a whole, or null when nothing is.</summary>
    public abstract string Validate(object item);
}
