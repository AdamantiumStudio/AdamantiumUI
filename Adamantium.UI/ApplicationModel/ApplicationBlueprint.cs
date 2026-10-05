using System;
using Adamantium.Core;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.ApplicationModel;

/// <summary>
/// What an application starts with, written in markup: its first window, its theme and language, how it shuts down,
/// its resources and its style sets. A project holds one; the build makes it a class and the entry point, and the
/// application reads it while it initializes. What the application's own code sets wins over it.
/// </summary>
public class ApplicationBlueprint : AdamantiumComponent, IApplicationBlueprint
{
    public static readonly AdamantiumProperty StartupWindowProperty = AdamantiumProperty.Register(nameof(StartupWindow),
        typeof(Type), typeof(ApplicationBlueprint), new PropertyMetadata(null));

    public static readonly AdamantiumProperty StartupThemeProperty = AdamantiumProperty.Register(nameof(StartupTheme),
        typeof(Type), typeof(ApplicationBlueprint), new PropertyMetadata(null));

    public static readonly AdamantiumProperty StartupThemeVariantProperty = AdamantiumProperty.Register(nameof(StartupThemeVariant),
        typeof(ThemeVariant), typeof(ApplicationBlueprint), new PropertyMetadata(default(ThemeVariant)));

    public static readonly AdamantiumProperty StartupLanguageProperty = AdamantiumProperty.Register(nameof(StartupLanguage),
        typeof(string), typeof(ApplicationBlueprint), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ShutDownModeProperty = AdamantiumProperty.Register(nameof(ShutDownMode),
        typeof(ShutDownMode), typeof(ApplicationBlueprint), new PropertyMetadata(ShutDownMode.OnMainWindowClosed));

    /// <summary>The window the application opens first.</summary>
    [TypeOf(typeof(Window))]
    public Type StartupWindow
    {
        get => GetValue<Type>(StartupWindowProperty);
        set => SetValue(StartupWindowProperty, value);
    }

    /// <summary>The theme the application opens on: Fluent (when unset), EditorPro, MacOs or one of the project's own.</summary>
    [TypeOf(typeof(Theme))]
    public Type StartupTheme
    {
        get => GetValue<Type>(StartupThemeProperty);
        set => SetValue(StartupThemeProperty, value);
    }

    /// <summary>The variant of the theme the application opens on: Light, Dark, System - which follows the operating
    /// system and keeps following it - or one of the theme's own; unset opens on the theme's default.</summary>
    public ThemeVariant StartupThemeVariant
    {
        get => GetValue<ThemeVariant>(StartupThemeVariantProperty);
        set => SetValue(StartupThemeVariantProperty, value);
    }

    ThemeVariant? IApplicationBlueprint.StartupThemeVariant => StartupThemeVariant.IsUnspecified ? null : StartupThemeVariant;

    /// <summary>The language the application opens in, by name ("en", "ru"); unset opens in the base language of its
    /// language files.</summary>
    public string StartupLanguage
    {
        get => GetValue<string>(StartupLanguageProperty);
        set => SetValue(StartupLanguageProperty, value);
    }

    /// <summary>When the application ends: with its main window, with its last window, or only when told.</summary>
    public ShutDownMode ShutDownMode
    {
        get => GetValue<ShutDownMode>(ShutDownModeProperty);
        set => SetValue(ShutDownModeProperty, value);
    }

    ShutDownMode? IApplicationBlueprint.ShutDownMode => IsSet(ShutDownModeProperty) ? ShutDownMode : null;

    /// <summary>Resources every part of the application finds by key, and the dictionaries they link.</summary>
    public ResourceDictionary Resources { get; set; }

    /// <summary>The application's own style sets, added to every theme.</summary>
    public StyleIncludeCollection StyleIncludes { get; } = new();
}
