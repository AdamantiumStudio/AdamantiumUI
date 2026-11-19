namespace Adamantium.UI.Core;

/// <summary>What writes a read-only property: its owner keeps it to itself, and gives the world only
/// <see cref="Property"/>, which reads.</summary>
public sealed class AdamantiumPropertyKey
{
    internal AdamantiumPropertyKey(AdamantiumProperty property)
    {
        Property = property;
    }

    /// <summary>The property the key writes - the one to read it, bind from it, and name in markup.</summary>
    public AdamantiumProperty Property { get; }
}
