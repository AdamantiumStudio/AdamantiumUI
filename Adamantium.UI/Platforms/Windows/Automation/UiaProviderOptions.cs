namespace Adamantium.UI.Platforms.Windows.Automation;

[System.Flags]
internal enum UiaProviderOptions
{
    ClientSideProvider = 0x1,
    ServerSideProvider = 0x2,
    NonClientAreaProvider = 0x4,
    OverrideProvider = 0x8,
    ProviderOwnsSetFocus = 0x10,
    UseComThreading = 0x20
}
