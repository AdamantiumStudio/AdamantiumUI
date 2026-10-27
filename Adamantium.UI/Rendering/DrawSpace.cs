using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>Where drawing lands: the projection onto the target and the bounds every scissor keeps within. A layer drawn
/// whole sets its own over its region (<see cref="Own"/>, and so is every space opened inside it), so what lies past the
/// window reaches its texture.</summary>
internal readonly record struct DrawSpace(Matrix4x4F Projection, Rect2D Bounds, bool Own);
