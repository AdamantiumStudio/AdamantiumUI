using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>One more placement a thing on the plane draws with, beside its main one, put wherever a function of the
/// camera says.</summary>
internal sealed class CanvasPlacedPart
{
    public readonly Placement Placement = new();

    /// <summary>Where the part goes for the camera as it is now.</summary>
    public Func<Matrix4x4F> Where;
}
