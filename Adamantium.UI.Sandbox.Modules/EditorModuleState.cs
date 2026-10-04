namespace Adamantium.UI.Sandbox.Modules;

/// <summary>Where a module stands with the editor and with the open document.</summary>
public enum EditorModuleState
{
    /// <summary>Part of this document: its tabs are in the ribbon, unless hidden.</summary>
    InDocument,

    /// <summary>In the editor, not in this document.</summary>
    Installed,

    /// <summary>Taken out of this document. What it made is still in the file and comes back with it.</summary>
    Detached,

    /// <summary>In the editor, with a newer version to be had.</summary>
    UpdateAvailable,

    /// <summary>To be had, not in the editor yet.</summary>
    Available
}
