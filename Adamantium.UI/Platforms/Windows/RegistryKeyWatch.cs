using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Tells whether a key of the current user has changed - a value set or taken out, a key under it added or
/// removed - since it was last asked. No thread of its own: Windows marks an event, and asking only looks at it; the
/// thread that asked for the notice ending is no change. A key that does not exist never changes.</summary>
internal sealed partial class RegistryKeyWatch : IDisposable
{
    private const uint NotifyChangeName = 0x1;
    private const uint NotifyChangeLastSet = 0x4;
    private const uint NotifyThreadAgnostic = 0x10000000;

    private readonly RegistryKey _key;
    private readonly AutoResetEvent _changed = new(false);

    /// <summary>Watches <paramref name="subKey"/> of the current user.</summary>
    public RegistryKeyWatch(string subKey)
    {
        _key = Registry.CurrentUser.OpenSubKey(subKey);
        Arm();
    }

    /// <summary>Whether the key has changed since the last ask (or since the watch began); asking takes the change.</summary>
    public bool TakeChange()
    {
        if (_key == null || !_changed.WaitOne(0))
        {
            return false;
        }

        Arm();
        return true;
    }

    public void Dispose()
    {
        _key?.Dispose();
        _changed.Dispose();
    }

    private void Arm()
    {
        if (_key != null)
        {
            RegNotifyChangeKeyValue(_key.Handle, false, NotifyChangeName | NotifyChangeLastSet | NotifyThreadAgnostic,
                _changed.SafeWaitHandle, true);
        }
    }

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint notifyFilter, SafeWaitHandle changed,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
