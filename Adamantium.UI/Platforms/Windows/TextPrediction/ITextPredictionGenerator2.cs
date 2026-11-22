using System;
using System.Runtime.InteropServices;
using Adamantium.Win32.WinRT;

namespace Adamantium.UI.Platforms.Windows.TextPrediction;

/// <summary>Windows.Data.Text.ITextPredictionGenerator2 (Windows 10 1803 and later): candidates of a kind, with the
/// words before, and the next word. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("b84723b8-2c77-486a-900a-a3453eedc15d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextPredictionGenerator2
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetCandidatesWithParametersAsync(IntPtr input, uint maxCandidates, TextPredictionOptions options,
        IStringIterable previousStrings, out IStringListOperation operation);
    [PreserveSig] int GetNextWordCandidatesAsync(uint maxCandidates, IStringIterable previousStrings,
        out IStringListOperation operation);
    [PreserveSig] int GetInputScope(out int inputScope);
    [PreserveSig] int SetInputScope(int inputScope);
}
