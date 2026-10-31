namespace Adamantium.UI.Core.Resources;

/// <summary>The bases a style of no theme gave one element, and the theme it found them in: they come off as they went
/// on, and are taken anew once another theme is in force there.</summary>
internal sealed class AppliedBases(Theme theme, IReadOnlyList<Style> bases)
{
    public Theme Theme { get; } = theme;

    public IReadOnlyList<Style> Bases { get; } = bases;
}
