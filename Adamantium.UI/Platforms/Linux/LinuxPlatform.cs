using System;
using System.Collections.Generic;
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

    /// <summary>The user's languages: their input sources' where the desktop says, the session's own first; else the
    /// session's.</summary>
    internal static IReadOnlyList<string> UserLanguages
    {
        get
        {
            if (_desktopSettings?.UserLanguages is not { Count: > 0 } sources)
            {
                return SessionLanguages.Value;
            }

            // Ordered once per list the portal gives: asked on every key, it allocates nothing.
            var ordered = _orderedLanguages;
            if (!ReferenceEquals(ordered?.Sources, sources))
            {
                ordered = new OrderedLanguages(sources, LinuxUserLanguages.PreferredFirst(sources, SessionLanguages.Value));
                _orderedLanguages = ordered;
            }

            return ordered.Languages;
        }
    }

    private static volatile OrderedLanguages _orderedLanguages;

    // The session's languages are its environment's, set before it started.
    private static readonly Lazy<IReadOnlyList<string>> SessionLanguages =
        new(() => LinuxUserLanguages.FromEnvironment(Environment.GetEnvironmentVariable));

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
                return (_desktopSettings?.PrefersDark, _desktopSettings?.FontFamily, _desktopSettings?.MonospaceFontFamily);
            });
        }
        FileDialog.Platform = new PortalFileDialog(owner =>
            (container.Resolve<IApplicationPlatform>() as IPortalParent)?.PortalParentOf(owner) ?? string.Empty);
        FileManager.Platform = new DesktopFileManager();
        if (OperatingSystem.IsLinux())
        {
            Trash.Platform = new FreedesktopTrash();
        }
        container.RegisterSingleton<ISpellChecker, EnchantSpellChecker>();
        container.RegisterSingleton<IWordPredictor, Hunspell.HunspellWordPredictor>();
        container.RegisterSingleton<IGamepadBackend, EvdevGamepadBackend>();
        container.RegisterSingleton<ISystemStartup, AutostartFolderStartup>();
        WaylandPlatform.Initialize(container);
    }
}
