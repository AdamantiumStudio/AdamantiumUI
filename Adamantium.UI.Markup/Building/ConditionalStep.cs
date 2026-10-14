namespace Adamantium.UI.Markup.Building;

/// <summary>Steps the generated code compiles only when a symbol is defined, such as <c>DEBUG</c>; the loader runs
/// them.</summary>
public sealed class ConditionalStep(string symbol) : BuildScope
{
    /// <summary>The compilation symbol.</summary>
    public string Symbol { get; } = symbol;
}
