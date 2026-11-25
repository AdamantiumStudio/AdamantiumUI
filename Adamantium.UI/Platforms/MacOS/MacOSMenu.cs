using System.Reflection;
using Adamantium.Localization;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Foundation;
using Adamantium.UI.Localization;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The menu bar's menus every Mac application has - its own, with hiding and quitting it, and that of its windows
/// - in the application's language.</summary>
internal static class MacOSMenu
{
    /// <summary>Shows the menus, from then on in the language in use. Main thread.</summary>
    internal static void Show(MacOSPlatform platform)
    {
        Show();
        Languages.Changed += (_, _) => platform.RunOnMainThread(Show);
    }

    private static string ApplicationName
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var product = assembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
            return string.IsNullOrEmpty(product) ? assembly?.GetName().Name ?? string.Empty : product;
        }
    }

    private static void Show()
    {
        var name = ApplicationName;
        var bar = NSMenu.Create(string.Empty);

        var application = NSMenu.Create(name);
        NSMenu.AddItem(application, MacOSMenuStrings.About(name), "orderFrontStandardAboutPanel:");
        NSMenu.AddSeparator(application);
        var services = NSMenu.Create(MacOSMenuStrings.Services);
        NSMenu.AddSubmenu(application, MacOSMenuStrings.Services, services);
        NSApplication.SetServicesMenu(services);
        NSMenu.AddSeparator(application);
        NSMenu.AddItem(application, MacOSMenuStrings.Hide(name), "hide:", "h");
        NSMenu.AddItem(application, MacOSMenuStrings.HideOthers, "hideOtherApplications:", "h",
            NSEventModifierFlags.Command | NSEventModifierFlags.Option);
        NSMenu.AddItem(application, MacOSMenuStrings.ShowAll, "unhideAllApplications:");
        NSMenu.AddSeparator(application);
        NSMenu.AddItem(application, MacOSMenuStrings.Quit(name), "terminate:", "q");
        NSMenu.AddSubmenu(bar, name, application);

        var window = NSMenu.Create(MacOSMenuStrings.Window);
        NSMenu.AddItem(window, MacOSMenuStrings.Minimize, "performMiniaturize:", "m");
        NSMenu.AddItem(window, MacOSMenuStrings.Zoom, "performZoom:");
        NSMenu.AddSeparator(window);
        NSMenu.AddItem(window, MacOSMenuStrings.BringAllToFront, "arrangeInFront:");
        NSMenu.AddSubmenu(bar, MacOSMenuStrings.Window, window);

        NSApplication.SetMainMenu(bar);
        NSApplication.SetWindowsMenu(window);
        NSObject.Release(services);
        NSObject.Release(application);
        NSObject.Release(window);
        NSObject.Release(bar);
    }
}
