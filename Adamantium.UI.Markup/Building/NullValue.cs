namespace Adamantium.UI.Markup.Building;

/// <summary>Nothing.</summary>
public sealed class NullValue : BuildValue
{
    /// <summary>The one null value.</summary>
    public static NullValue Instance { get; } = new();
}
