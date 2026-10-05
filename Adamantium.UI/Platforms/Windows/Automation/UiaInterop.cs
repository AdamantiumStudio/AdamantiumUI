using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.UI.Platforms.Windows.Automation;

internal static unsafe partial class UiaInterop
{
    public const int RootObjectId = -25;
    public const int AppendRuntimeId = 3;
    public const ushort VariantInt = 3;
    public const ushort VariantUnknown = 13;
    public const ushort VariantDouble = 5;

    private const string Core = "UIAutomationCore.dll";
    private const string OleAutomation = "oleaut32.dll";

    [LibraryImport(Core)]
    public static partial nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, IRawElementProviderSimple provider);

    [LibraryImport(Core)]
    public static partial int UiaHostProviderFromHwnd(nint hwnd, out IRawElementProviderSimple provider);

    [LibraryImport(Core)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();

    [LibraryImport(Core)]
    public static partial int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [LibraryImport(Core)]
    public static partial int UiaRaiseAutomationPropertyChangedEvent(IRawElementProviderSimple provider, int propertyId,
        UiaVariant oldValue, UiaVariant newValue);

    [LibraryImport(Core)]
    public static partial int UiaRaiseStructureChangedEvent(IRawElementProviderSimple provider, UiaStructureChangeType changeType,
        int* runtimeId, int runtimeIdLength);

    [LibraryImport(Core)]
    public static partial int UiaDisconnectProvider(IRawElementProviderSimple provider);

    [LibraryImport(Core)]
    public static partial int UiaGetReservedNotSupportedValue(out nint notSupported);

    [LibraryImport(OleAutomation)]
    public static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint count);

    [LibraryImport(OleAutomation)]
    public static partial int SafeArrayPutElement(nint array, int* index, void* value);
}
