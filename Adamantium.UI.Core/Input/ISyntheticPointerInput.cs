using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Core.Input;

/// <summary>The platform's own synthetic pointer input: fingers or a pen handed to the system, which raises them as its
/// real pointer messages at their desktop points - in whatever window is on top there. It goes through the system's
/// input, so it is for checking a platform's pointer path, never for driving an application while someone uses the
/// computer.</summary>
public interface ISyntheticPointerInput
{
    /// <summary>Plays a gesture, frame by frame, and returns once the system has taken them all. Each frame holds every
    /// contact down at that moment, the same contact at the same index in every frame: one for a pen, up to ten fingers.
    /// <see cref="PointerType.Mouse"/> is not a contact and is refused.</summary>
    void Inject(PointerType type, IReadOnlyList<IReadOnlyList<SyntheticPointerFrame>> frames);
}

/// <summary>One contact in one frame of a synthetic gesture: it went down, moved, went up, or a pen went out of range
/// (<see cref="RawPointerEventType.LeaveWindow"/>), at a desktop point.</summary>
public readonly record struct SyntheticPointerFrame(RawPointerEventType Kind, PixelPoint Screen, PointerPointProperties Properties);
