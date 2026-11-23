using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Adamantium.UITests;

/// <summary>A key of the current user's for one test, under Software\AdamantiumTests; disposing it deletes it, and
/// the tests' key too once nothing else is in it.</summary>
[SupportedOSPlatform("windows")]
internal sealed class TestRegistryKey : IDisposable
{
    private const string TestsKey = @"Software\AdamantiumTests";

    public TestRegistryKey()
    {
        Path = TestsKey + @"\" + Guid.NewGuid().ToString("N");
        Registry.CurrentUser.CreateSubKey(Path).Dispose();
    }

    /// <summary>The key's path under the current user.</summary>
    public string Path { get; }

    /// <summary>Sets the string <paramref name="name"/> of the key to <paramref name="value"/>.</summary>
    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, true);
        key!.SetValue(name, value);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Path, false);
        using (var tests = Registry.CurrentUser.OpenSubKey(TestsKey))
        {
            if (tests is not { SubKeyCount: 0, ValueCount: 0 })
                return;
        }

        Registry.CurrentUser.DeleteSubKey(TestsKey, false);
    }
}
