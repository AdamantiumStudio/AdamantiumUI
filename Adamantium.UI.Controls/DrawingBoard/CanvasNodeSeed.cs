using System;
using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What a loaded node needs to come back as itself: the <see cref="Kind"/> and <see cref="Payload"/> the engine
/// round-trips unread. Without an application answer, a plain node is made.</summary>
public sealed class CanvasNodeSeed
{
    /// <summary>What the application calls this sort of node. Empty for a node nobody claimed.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Whatever the application needs to make this node again, as text it chose the shape of. Never read by
    /// the engine.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>What the node says at the top.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Where and how big, in WORLD units - never screen pixels, which mean nothing at another zoom.</summary>
    public Rect World { get; init; }

    public IReadOnlyList<CanvasSocketSeed> Inputs { get; init; } = Array.Empty<CanvasSocketSeed>();

    public IReadOnlyList<CanvasSocketSeed> Outputs { get; init; } = Array.Empty<CanvasSocketSeed>();
}
