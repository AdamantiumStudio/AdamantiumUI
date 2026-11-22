using System;
using System.Runtime.InteropServices;
using Adamantium.Win32.WinRT;

namespace Adamantium.UI.Platforms.Windows.TextPrediction;

/// <summary>Windows.Data.Text.ITextPredictionGenerator: the language it predicts in and the words a text may become.
/// The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("5eacab07-abf1-4cb6-9d9e-326f2b468756")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextPredictionGenerator
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetResolvedLanguage(out IntPtr language);
    [PreserveSig] int GetLanguageAvailableButNotInstalled([MarshalAs(UnmanagedType.U1)] out bool notInstalled);
    [PreserveSig] int GetCandidatesAsync(IntPtr input, out IStringListOperation operation);
    [PreserveSig] int GetCandidatesWithMaxCountAsync(IntPtr input, uint maxCandidates, out IStringListOperation operation);
}
