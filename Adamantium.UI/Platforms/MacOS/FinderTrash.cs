using System;
using System.IO;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The Trash, where the Finder can put an item back from.</summary>
internal sealed class FinderTrash : ITrashPlatform
{
    public bool MoveToTrash(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            return NSFileManager.MoveToTrash(Path.GetFullPath(path));
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }
}
