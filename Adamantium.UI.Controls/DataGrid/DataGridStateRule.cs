using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>Derives what a cell means ("over the limit", "settled") from its value, for the theme's triggers to style; the
/// counterpart of <see cref="DataGridColumn.StateBinding"/>. Returns a meaning, never a color.</summary>
public abstract class DataGridStateRule : FundamentalUIComponent
{
    /// <summary>What <paramref name="value"/> means, or null when it means nothing in particular.
    /// <paramref name="item"/> is the whole record, so a rule can weigh one field against another.</summary>
    public abstract object State(object value, object item);
}
