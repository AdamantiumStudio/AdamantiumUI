namespace Adamantium.UI.Controls.Text;

/// <summary>Which editing command of its text box a menu row runs (<see cref="TextBoxBase.EditActionProperty"/>).</summary>
public enum TextEditAction
{
    /// <summary>None: the row is the application's own.</summary>
    None,

    Cut,

    Copy,

    Paste,

    SelectAll
}
