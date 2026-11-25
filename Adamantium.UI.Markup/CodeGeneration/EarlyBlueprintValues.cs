namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What the start reads of an application blueprint before the blueprint is made - its resources are not
/// wanted that early - written onto the attribute that names the blueprint to the assembly.</summary>
public sealed class EarlyBlueprintValues
{
    /// <summary>The splash window, as generated code writes it; null for none.</summary>
    public string SplashWindow { get; set; }

    /// <summary>The startup window, as generated code writes it; null for none. The splash window appears where it will.
    /// </summary>
    public string StartupWindow { get; set; }

    /// <summary>The startup theme, as generated code writes it; null for none. The splash window's close button looks as
    /// that theme's.</summary>
    public string StartupTheme { get; set; }

    /// <summary>The limit on copies running at once; 0 for none. A copy past it ends before anything is made.</summary>
    public int MaxInstances { get; set; }

    /// <summary>Whether the graphics device is made with its debug layer; it is made before the blueprint.</summary>
    public bool EnableGraphicsDebug { get; set; }

    /// <summary>The presentation policy, by the name of its value; null for none. The splash window presents before the
    /// blueprint is made.</summary>
    public string PresentPolicy { get; set; }
}
