namespace Adamantium.UI.Markup.Building;

/// <summary>A value made once for a place in the markup and kept for every build of it: its steps, ending in a return, run
/// the first time a <see cref="SharedValue"/> reads it. The generated class keeps it in a static field; the loader, for
/// as long as it lives.</summary>
public sealed class SharedValueStep(TypeValue type, string name) : BuildScope
{
    /// <summary>The type of the value.</summary>
    public TypeValue Type { get; } = type;

    /// <summary>The name its field and its maker are called by.</summary>
    public string Name { get; } = name;
}
