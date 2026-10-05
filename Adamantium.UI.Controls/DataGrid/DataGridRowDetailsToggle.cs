using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>Opens and shuts a row's details panel, in a pinned column of its own, separate from the tree expander since a row
/// can have both children and a panel.</summary>
public class DataGridRowDetailsToggle : ContentControl
{
    /// <summary>Whether this row's panel is open - the theme turns the sign from + to − on it.</summary>
    public static readonly AdamantiumProperty IsOpenProperty = AdamantiumProperty.Register(nameof(IsOpen),
        typeof(bool), typeof(DataGridRowDetailsToggle),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    /// <summary>This row has no panel to open: a group's caption, or the panel row itself. It keeps the column's width -
    /// a strip that closed up under some rows would make the table's left edge ragged - and shows no sign at all.</summary>
    public static readonly AdamantiumProperty IsBlankProperty = AdamantiumProperty.Register(nameof(IsBlank),
        typeof(bool), typeof(DataGridRowDetailsToggle),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public bool IsOpen
    {
        get => GetValue<bool>(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool IsBlank
    {
        get => GetValue<bool>(IsBlankProperty);
        set => SetValue(IsBlankProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DataGridRowDetailsToggleAutomationPeer(this);
}
