using Adamantium.Core.DependencyInjection;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Input.Evdev;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Wayland;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>What a Linux desktop gives the application whatever draws its windows - spell checking, gamepads - and then
/// the windows themselves, on Wayland.</summary>
public static class LinuxPlatform
{
    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<ISpellChecker, EnchantSpellChecker>();
        resolver.RegisterSingleton<IGamepadBackend, EvdevGamepadBackend>();
        WaylandPlatform.Initialize(resolver);
    }
}
