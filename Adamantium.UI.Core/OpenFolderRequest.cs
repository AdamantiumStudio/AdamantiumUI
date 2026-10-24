using System;

namespace Adamantium.UI.Core;

/// <summary>What to ask the user when choosing a folder, such as where something new goes.</summary>
public sealed class OpenFolderRequest
{
    /// <summary>The dialog's caption. Left unset, the platform uses its own wording.</summary>
    public string Title { get; init; }

    /// <summary>What this dialog is for - see <see cref="OpenFileRequest.Key"/>. Its size, place and last folder are
    /// remembered under it.</summary>
    public string Key { get; init; }

    /// <summary>The window this is being asked on behalf of - see <see cref="OpenFileRequest.Owner"/>.</summary>
    public IntPtr Owner { get; init; }
}
