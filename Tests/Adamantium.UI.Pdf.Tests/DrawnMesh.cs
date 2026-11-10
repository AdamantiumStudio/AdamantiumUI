using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A colored mesh drawn, inside <see cref="Depth"/> groups.</summary>
internal sealed record DrawnMesh(ColoredMesh Mesh, Matrix4x4<float> Transform, int Depth);
