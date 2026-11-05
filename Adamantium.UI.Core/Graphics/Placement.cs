using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Graphics;

/// <summary>Where an element puts the drawings it gives this placement, on top of its own world transform. Writing
/// <see cref="Matrix"/> moves them without drawing anything again: only the matrix reaches the GPU. A camera works this
/// way - each object keeps its geometry and the control rewrites the object's placement as the view moves.</summary>
/// <remarks>A placement belongs to the one element that draws with it, and is written on the thread that renders that
/// element. The element's box still decides where its drawings are culled and ordered, so placed drawings belong inside
/// it.</remarks>
public sealed class Placement
{
    private Matrix4x4F _matrix = Matrix4x4F.Identity;
    private IUIComponent _owner;
    private long _version;

    /// <summary>Where the drawings go, in the element's own space.</summary>
    public Matrix4x4F Matrix
    {
        get => _matrix;
        set
        {
            // No equality check: Matrix4x4F compares near-equal, which would drop a sub-pixel step of the view.
            _matrix = value;
            _version++;
            if (_owner != null)
            {
                RenderDirty.MarkPlacement(_owner, this);
            }
        }
    }

    internal Guid Id { get; } = Guid.NewGuid();

    internal IUIComponent Owner => _owner;

    /// <summary>Counts the writes of <see cref="Matrix"/>, so a renderer keeps whichever value is newest.</summary>
    internal long Version => _version;

    /// <summary>Binds the placement to the element drawing with it; a second element still in the tree may not.</summary>
    internal void DrawnBy(IUIComponent owner)
    {
        if (_owner != null && !ReferenceEquals(_owner, owner) && _owner.IsAttachedToVisualTree)
        {
            throw new InvalidOperationException("A placement belongs to the one element that draws with it.");
        }

        _owner = owner;
    }
}
