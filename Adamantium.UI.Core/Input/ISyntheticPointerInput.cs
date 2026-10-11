using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Core.Input;

/// <summary>The platform's own synthetic pointer input: a finger or a pen handed to the system, which raises it as its
/// real pointer messages at their desktop points - in whatever window is on top there. It goes through the system's
/// input, so it is for checking a platform's pointer path, never for driving an application while someone uses the
/// computer.</summary>
public interface ISyntheticPointerInput
{
    /// <summary>Plays one contact's frames, in order, as one gesture, and returns once the system has taken them all.
    /// <see cref="PointerType.Mouse"/> is not a contact and is refused.</summary>
    void Inject(PointerType type, IReadOnlyList<SyntheticPointerFrame> frames);
}

/// <summary>One frame of a synthetic contact: it went down, moved, went up, or a pen went out of range
/// (<see cref="RawPointerEventType.LeaveWindow"/>), at a desktop point.</summary>
public readonly record struct SyntheticPointerFrame(RawPointerEventType Kind, PixelPoint Screen, PointerPointProperties Properties);
