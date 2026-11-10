namespace Adamantium.UI.WebView;

/// <summary>What a page asks the user to choose with its file field.</summary>
public enum WebFileDialogMode
{
    /// <summary>One file to open: <c>&lt;input type=file&gt;</c>.</summary>
    Open,

    /// <summary>Files to open, one or more: <c>&lt;input type=file multiple&gt;</c>.</summary>
    OpenMultiple,

    /// <summary>A folder, whose files the page gets: <c>&lt;input type=file webkitdirectory&gt;</c>.</summary>
    OpenFolder,

    /// <summary>Where to save a file the page writes.</summary>
    Save
}
