using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A path drawn, inside <see cref="Depth"/> groups.</summary>
internal sealed record DrawnGeometry(Brush Brush, Geometry Geometry, Pen Pen, Matrix4x4<float> Transform, int Depth);
