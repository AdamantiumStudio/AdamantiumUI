using System;
using Adamantium.Graphics.Core.Presentation;

namespace Adamantium.UI.ApplicationModel;

/// <summary>Names the blueprint an application assembly starts from. Written by the build, never by hand: the
/// application looks for it in its own assembly while it initializes.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ApplicationBlueprintAttribute : Attribute
{
    public ApplicationBlueprintAttribute(Type blueprintType)
    {
        BlueprintType = blueprintType;
    }

    public Type BlueprintType { get; }

    /// <summary>The blueprint's splash window: read without making the blueprint, whose resources are not wanted that
    /// early.</summary>
    public Type SplashWindow { get; set; }

    /// <summary>The blueprint's startup window, read as early: the splash window appears where it will.</summary>
    public Type StartupWindow { get; set; }

    /// <summary>The blueprint's startup theme, read as early: the splash window's close button looks as that theme's.
    /// Null when it states none.</summary>
    public Type StartupTheme { get; set; }

    /// <summary>The blueprint's limit on copies running at once, read before anything starts: a copy past it ends at once.
    /// 0 has none.</summary>
    public int MaxInstances { get; set; }

    /// <summary>Whether the blueprint makes the graphics device with its debug layer, read as early: the device is made
    /// before the blueprint.</summary>
    public bool EnableGraphicsDebug { get; set; }

    /// <summary>The blueprint's presentation policy, read as early: the splash window presents before the blueprint is
    /// made. Inherit when it states none.</summary>
    public PresentPolicy PresentPolicy { get; set; }
}
