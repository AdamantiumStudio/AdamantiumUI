namespace Adamantium.UI.Platforms.Linux.Accessibility;

/// <summary>The states of AT-SPI the bridge reports, by their bit numbers in the state set.</summary>
internal enum AtSpiState
{
    Active = 1,
    Checked = 4,
    Collapsed = 5,
    Editable = 7,
    Enabled = 8,
    Expandable = 9,
    Expanded = 10,
    Focusable = 11,
    Focused = 12,
    Modal = 16,
    MultiLine = 17,
    MultiSelectable = 18,
    Selectable = 22,
    Selected = 23,
    Sensitive = 24,
    Showing = 25,
    SingleLine = 26,
    Visible = 30,
    Indeterminate = 32,
    Checkable = 41,
    ReadOnly = 43,
}
