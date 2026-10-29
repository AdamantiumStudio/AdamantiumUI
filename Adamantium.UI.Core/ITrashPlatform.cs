namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="Trash"/>: the desktop's trash or recycle bin.</summary>
public interface ITrashPlatform
{
    /// <summary>Moves the file or folder at <paramref name="path"/> to the trash; false when it has no trash to go to,
    /// and is left where it is.</summary>
    bool MoveToTrash(string path);
}
