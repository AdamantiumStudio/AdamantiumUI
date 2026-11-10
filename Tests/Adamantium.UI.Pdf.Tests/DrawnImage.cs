using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>An image drawn by its unit square, inside <see cref="Depth"/> groups.</summary>
internal sealed record DrawnImage(ImageSource Image, Matrix4x4<float> Transform, int Depth);
