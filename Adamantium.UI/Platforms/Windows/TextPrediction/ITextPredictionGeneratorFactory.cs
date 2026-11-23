using System;
using System.Runtime.InteropServices;

namespace Adamantium.UI.Platforms.Windows.TextPrediction;

/// <summary>Makes a Windows.Data.Text.TextPredictionGenerator for a language. The first three methods are
/// IInspectable's.</summary>
[ComImport]
[Guid("7257b416-8ba2-4751-9d30-9d85435653a2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextPredictionGeneratorFactory
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int Create(IntPtr languageTag, out ITextPredictionGenerator generator);
}
