using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// A keyframe animation (CSS <c>@keyframes</c> analog, own implementation): interpolates the double properties named in
/// its <see cref="KeyFrames"/>' setters through those keyframes over <see cref="Timeline.Duration"/>, honoring
/// <see cref="Timeline.Delay"/>, <see cref="Timeline.IterationCount"/> (<see cref="double.PositiveInfinity"/> = loop) and
/// <see cref="Timeline.AutoReverse"/> (ping-pong). Start it with <see cref="Timeline.Apply"/>.
/// </summary>
public class Animation : Timeline
{
    /// <summary>The keyframes, by <see cref="KeyFrame.Cue"/> (order is normalized at run time). Markup content, so
    /// they are written directly inside &lt;Animation&gt;.</summary>
    [Content]
    public List<KeyFrame> KeyFrames { get; } = new();

    /// <summary>Hook for a prebuilt animation TYPE (e.g. <c>PulseAnimation</c>) to synthesize its <see cref="KeyFrames"/>
    /// from friendly properties (Min/Max/...) just before it runs. The base keyframe animation authors its frames in
    /// markup, so this is a no-op for it.</summary>
    protected virtual void Prepare() { }

    /// <inheritdoc />
    protected override void Begin(AdamantiumComponent target, Action completed, double resumeElapsed)
    {
        Prepare();
        AnimationManager.BeginKeyFrame(target, this, completed, resumeElapsed);
    }

    /// <inheritdoc />
    protected override void CopyContentTo(Timeline copy) => ((Animation)copy).KeyFrames.AddRange(KeyFrames);
}
