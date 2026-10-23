using System;

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
}
