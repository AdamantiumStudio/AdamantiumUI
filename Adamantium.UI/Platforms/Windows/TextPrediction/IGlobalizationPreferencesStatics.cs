using System;
using System.Runtime.InteropServices;
using Adamantium.Win32.WinRT;

namespace Adamantium.UI.Platforms.Windows.TextPrediction;

/// <summary>Windows.System.UserProfile.GlobalizationPreferences' static members: the user's languages, the ones Windows'
/// typing suggestions come with. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("01bf4326-ed37-4e96-b0e9-c1340d1ea158")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGlobalizationPreferencesStatics
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetCalendars(out IStringVectorView calendars);
    [PreserveSig] int GetClocks(out IStringVectorView clocks);
    [PreserveSig] int GetCurrencies(out IStringVectorView currencies);
    [PreserveSig] int GetLanguages(out IStringVectorView languages);
}
