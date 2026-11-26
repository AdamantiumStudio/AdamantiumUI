using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Printing;

/// <summary>A picture of part of an element that is not vector: <see cref="Region"/>, in the element's own units, as
/// the GPU drew it - the picture runs to the whole pixel past the region's far edges, and only the region of it is laid
/// down, so squares side by side meet without overlapping.</summary>
internal sealed record RasterTile(Rect Region, ImageSource Image);
