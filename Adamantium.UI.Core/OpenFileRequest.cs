using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core;

/// <summary>What to ask the user when opening. No suggested name: a file being opened is one that already exists, and
/// the only thing worth suggesting about it is where to look.</summary>
public sealed class OpenFileRequest
{
    /// <summary>The dialog's caption. Left unset, the platform uses its own wording for opening.</summary>
    public string Title { get; init; }

    /// <summary>The kinds of file on offer, first one selected.</summary>
    public IReadOnlyList<FileType> FileTypes { get; init; }

    /// <summary>An application-chosen name (e.g. "table.export") under which the platform remembers this dialog's size,
    /// position and folder; unset, all dialogs share one.</summary>
    public string Key { get; init; }

    /// <summary>The owner window the dialog blocks and opens over, which decides its monitor; unset uses the foreground
    /// window.</summary>
    public IntPtr Owner { get; init; }
}
