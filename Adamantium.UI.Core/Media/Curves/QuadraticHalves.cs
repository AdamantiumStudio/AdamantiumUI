namespace Adamantium.UI.Core.Media.Curves;

/// <summary>A quadratic curve split in two: the part before the cut and the part after it.</summary>
/// <param name="First">From the curve's start to the cut.</param>
/// <param name="Second">From the cut to the curve's end.</param>
internal readonly record struct QuadraticHalves(QuadraticCurve First, QuadraticCurve Second);
