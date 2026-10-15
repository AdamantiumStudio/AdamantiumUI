using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering.Verification;

/// <summary>What the tree says about one component at the moment a frame is recorded. <see cref="Animated"/>: the render
/// thread plays its transform or opacity; <see cref="Carried"/>: it or an ancestor is animated that way, so its world
/// runs ahead of the tree's; <see cref="Clones"/>: how many copies of it the tree asks for (RenderClones), drawn only when
/// it is not <see cref="Hidden"/>.</summary>
internal readonly record struct TreeFact(
    Matrix4x4F Local,
    Size Size,
    bool Clips,
    IUIComponent Parent,
    float Opacity,
    Matrix4x4F World,
    bool Animated,
    bool Carried,
    int Clones,
    bool Hidden,
    float SelfOpacity,
    Vector4F? TextColor,
    bool Pending);
