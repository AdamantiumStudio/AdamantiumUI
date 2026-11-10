namespace Adamantium.UI.Rendering.Payloads;

/// <summary>Where in a group of draws a mark stands. A group is recorded as
/// <c>Begin, (MaskBegin, mask draws, MaskEnd)*, ContentBegin, content draws, End</c>: its masks are ordinary draws
/// between their marks, drawn into a mask's target instead of the group's.</summary>
public enum GroupMarkKind
{
    Begin,
    MaskBegin,
    MaskEnd,
    ContentBegin,
    End,
}
