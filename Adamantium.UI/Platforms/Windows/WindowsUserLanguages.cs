using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.UI.Platforms.Windows.TextPrediction;
using Adamantium.Win32.WinRT;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The languages the user added to Windows (Windows.System.UserProfile.GlobalizationPreferences), most wanted
/// first. Kept once read and read again only after the user's language list in the registry changed, so asking costs
/// a glance at an event; it may be asked from any thread.</summary>
internal sealed class WindowsUserLanguages : IDisposable
{
    private const string PreferencesClass = "Windows.System.UserProfile.GlobalizationPreferences";

    private readonly Lock _gate = new();
    private readonly RegistryKeyWatch _watch;
    private IReadOnlyList<string> _languages;

    /// <summary>Languages read again as <paramref name="userLanguagesKey"/> (of the current user) changes.</summary>
    public WindowsUserLanguages(string userLanguagesKey) => _watch = new RegistryKeyWatch(userLanguagesKey);

    /// <summary>The user's languages as they are now, as language tags ("ru-RU").</summary>
    public IReadOnlyList<string> Current
    {
        get
        {
            lock (_gate)
            {
                // The change is taken first: a list read before it was asked would otherwise stand for good.
                if (_watch.TakeChange() || _languages == null)
                {
                    _languages = Read();
                }

                return _languages;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _watch.Dispose();
        }
    }

    /// <summary>Reads the user's languages from Windows as they are now; none where Windows has no such list.</summary>
    public static List<string> Read()
    {
        List<string> found = [];
        var preferences = WinRTInterop.GetActivationFactory<IGlobalizationPreferencesStatics>(PreferencesClass);
        if (preferences == null)
        {
            return found;
        }

        IStringVectorView languages = null;
        try
        {
            if (preferences.GetLanguages(out languages) != 0 || languages == null || languages.GetSize(out var size) != 0)
            {
                return found;
            }

            for (uint index = 0; index < size; index++)
            {
                if (languages.GetAt(index, out var item) != 0)
                {
                    continue;
                }

                try
                {
                    found.Add(WinRTInterop.ReadString(item));
                }
                finally
                {
                    WinRTInterop.DeleteString(item);
                }
            }

            return found;
        }
        finally
        {
            if (languages != null)
            {
                Marshal.ReleaseComObject(languages);
            }

            Marshal.ReleaseComObject(preferences);
        }
    }
}
