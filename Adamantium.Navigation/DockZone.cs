using System;

namespace Adamantium.Navigation;

/// <summary>Where a pane sits or a drop would put it (one bit, a pane's <c>Zone</c>), or combined, where it may be (its
/// <c>Allowed</c>). A combination read as a place counts as its first bit.</summary>
[Flags]
public enum DockZone
{
    None = 0,

    /// <summary>The main area - where documents live.</summary>
    Center = 1 << 0,

    Left = 1 << 1,

    Top = 1 << 2,

    Right = 1 << 3,

    Bottom = 1 << 4,

    /// <summary>Not docked at all: a root of its own, in its own window.</summary>
    Floating = 1 << 5,

    /// <summary>Any edge - not the document area, not floating.</summary>
    Edges = Left | Top | Right | Bottom,

    All = Center | Edges | Floating
}
