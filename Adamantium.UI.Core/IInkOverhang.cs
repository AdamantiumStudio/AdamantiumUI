using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

/// <summary>An element whose drawing reaches past its own box - a text's shadow or reflection - so the renderer keeps
/// drawing it while that part is still in view, though the box has gone out.</summary>
public interface IInkOverhang
{
    /// <summary>How far the drawing reaches past each side of the element's box, in its units.</summary>
    Thickness InkOverhang { get; }
}
