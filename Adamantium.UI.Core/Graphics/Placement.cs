using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Graphics;

/// <summary>Where an element puts the drawings it gives this placement, on top of its own world transform. Writing
/// <see cref="Matrix"/> moves them without drawing anything again: only the matrix reaches the GPU. A camera works this
/// way - each object keeps its geometry and the control rewrites the object's placement as the view moves.</summary>
/// <remarks>A placement belongs to the one element that draws with it.</remarks>
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

    /// <summary>Counts the writes of <see cref="Matrix"/>, so the renderer keeps whichever value is newest.</summary>
    internal long Version => _version;

    /// <summary>The matrix the renderer draws with and its version - written on the render thread only.</summary>
    internal Matrix4x4F Applied = Matrix4x4F.Identity;

    internal long AppliedVersion = -1;

    internal void DrawnBy(IUIComponent owner) => _owner = owner;

    /// <summary>Takes <paramref name="matrix"/> as what is drawn, if it is newer than what is.</summary>
    internal bool Apply(in Matrix4x4F matrix, long version)
    {
        if (version <= AppliedVersion)
        {
            return false;
        }

        Applied = matrix;
        AppliedVersion = version;
        return true;
    }
}
