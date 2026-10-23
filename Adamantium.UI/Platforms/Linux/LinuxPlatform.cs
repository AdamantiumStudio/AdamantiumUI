using Adamantium.Core.DependencyInjection;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Input.Evdev;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Wayland;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>What a Linux desktop gives the application whatever draws its windows - the desktop's settings and file
/// dialogs through its portals, spell checking, gamepads - and then the windows themselves, on Wayland.</summary>
public static class LinuxPlatform
{
    private static DesktopPortalSettings _desktopSettings;
    private static bool _desktopSettingsRead;

    public static void Initialize(IDependencyContainer container)
    {
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
