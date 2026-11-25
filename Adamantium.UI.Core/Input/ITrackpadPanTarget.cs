namespace Adamantium.UI.Core.Input;

/// <summary>An element that takes gestures and says itself where two fingers on a trackpad pan it rather than turn the
/// wheel - whatever it holds under them, which <see cref="Gestures.IsTrackpadPanEnabledProperty"/> does not allow for.
/// </summary>
public interface ITrackpadPanTarget
{
    /// <summary>Whether a pan over <paramref name="hit"/>, the element itself or one it holds, is its.</summary>
    bool TakesTrackpadPan(IInputComponent hit);
}
