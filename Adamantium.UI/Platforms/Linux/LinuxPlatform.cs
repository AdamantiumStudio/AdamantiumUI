using System;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Input.Evdev;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Linux.Accessibility;
using Adamantium.UI.Platforms.Wayland;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>What a Linux desktop gives the application whatever draws its windows - the desktop's settings and file
/// dialogs through its portals, spell checking, gamepads - and then the windows themselves, on Wayland.</summary>
public static class LinuxPlatform
{
    private const string NoAccessibilityBridge = "NO_AT_BRIDGE";
    private const string Set = "1";

    private static DesktopPortalSettings _desktopSettings;
    private static bool _desktopSettingsRead;
    private static bool _desktopEntryWritten;
    private static Task<AtSpiBridge> _accessibility;

    public static void Initialize(IDependencyContainer container)
    {
        if (_accessibility == null && Environment.GetEnvironmentVariable(NoAccessibilityBridge) != Set)
        {
            _accessibility = AtSpiBridge.StartAsync();
        }

        if (!_desktopEntryWritten)
        {
            _desktopEntryWritten = true;
            DesktopEntry.WriteInBackground(WaylandPlatform.AppId);
        }

        if (!_desktopSettingsRead)
        {
            _desktopSettingsRead = true;
            SystemAppearance.ReadInBackground(() =>
            {
                _desktopSettings = DesktopPortalSettings.Open();
                return (_desktopSettings?.PrefersDark, _desktopSettings?.FontFamily);
            });
        }
        FileDialog.Platform = new PortalFileDialog(owner =>
            (container.Resolve<IApplicationPlatform>() as IPortalParent)?.PortalParentOf(owner) ?? string.Empty);
        container.RegisterSingleton<ISpellChecker, EnchantSpellChecker>();
        container.RegisterSingleton<IGamepadBackend, EvdevGamepadBackend>();
        WaylandPlatform.Initialize(container);
    }
}
