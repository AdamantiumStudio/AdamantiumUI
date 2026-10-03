namespace Adamantium.UI.Sandbox.Models;

/// <summary>One thing dropped into the demo: the format it was read in and what it carried (the size, for a
/// picture).</summary>
public sealed record DroppedEntry(DroppedKind Kind, object Value);

/// <summary>The format a dropped payload was read in.</summary>
public enum DroppedKind
{
    File,
    Image,
    Html,
    Rtf,
    PlainText,
    InApp,
}
