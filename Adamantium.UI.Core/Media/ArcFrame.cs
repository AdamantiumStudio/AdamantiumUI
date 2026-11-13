using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>An elliptical arc seen in the frame where it is a circle.</summary>
/// <param name="Back">The map from that frame back to the figure's own units.</param>
/// <param name="Center">The circle's center in that frame.</param>
/// <param name="Radius">The circle's radius in that frame: the arc's vertical radius.</param>
/// <param name="RadiusX">The arc's horizontal radius, as large as the arc needs to reach its end.</param>
/// <param name="StartAngle">Where the arc starts on the circle, in degrees.</param>
/// <param name="Range">How far it turns, in degrees.</param>
/// <param name="Sign">Which way it turns: 1 clockwise, -1 counterclockwise.</param>
internal readonly record struct ArcFrame(Matrix3x2<double> Back, Vector2<double> Center, double Radius, double RadiusX,
    double StartAngle, double Range, double Sign);
