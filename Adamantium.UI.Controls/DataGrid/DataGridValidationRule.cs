using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>Whether a cell's value is acceptable, asked of every row, so it holds no per-row state. Returns a message, not a
/// color; null or empty means valid.</summary>
public abstract class DataGridValidationRule : FundamentalUIComponent
{
    /// <summary>What is wrong with <paramref name="value"/>, or null when nothing is. <paramref name="item"/> is the
    /// whole record, so a rule can weigh one field against another.</summary>
    public abstract string Validate(object value, object item);
}
