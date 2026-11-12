namespace Adamantium.UI.Core.Media.Curves;

/// <summary>One figure of a <see cref="PathCurves"/>: its curves are <see cref="Count"/> from <see cref="First"/>.
/// </summary>
/// <param name="First">The index of its first curve.</param>
/// <param name="Count">How many curves it has.</param>
/// <param name="IsClosed">Whether its outline goes back to its start; a fill closes it either way.</param>
/// <param name="IsFilled">Whether it takes part in the fill.</param>
internal readonly record struct CurveFigure(int First, int Count, bool IsClosed, bool IsFilled);
