using System;
using Adamantium.Core;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.ApplicationModel;

/// <summary>What an application starts with, as the application reads it while it initializes. A value the blueprint
/// does not state is null: the application keeps its own.</summary>
public interface IApplicationBlueprint
{
    /// <summary>The window the application opens first.</summary>
    Type StartupWindow { get; }

    /// <summary>The theme the application opens on.</summary>
    Type StartupTheme { get; }

    /// <summary>The variant of the theme the application opens on: Light, Dark, System or one of the theme's own.</summary>
    ThemeVariant? StartupThemeVariant { get; }

    /// <summary>The language the application opens in, by name.</summary>
    string StartupLanguage { get; }

    /// <summary>When the application ends.</summary>
    ShutDownMode? ShutDownMode { get; }

    /// <summary>Resources every part of the application finds by key, and the dictionaries they link.</summary>
    ResourceDictionary Resources { get; }

    /// <summary>The application's own style sets, added to every theme.</summary>
    StyleIncludeCollection StyleIncludes { get; }
}
