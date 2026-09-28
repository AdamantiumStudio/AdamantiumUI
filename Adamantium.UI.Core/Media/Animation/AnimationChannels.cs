namespace Adamantium.UI.Core.Media.Animation;

/// <summary>Decides which <see cref="CompositorChannel"/> an animation belongs to, from what its property touches: a
/// <see cref="Transform"/> or an <see cref="PropertyMetadataOptions.AffectsPaint"/> property; the rest stays on the loop.</summary>
public static class AnimationChannels
{
    /// <summary>The channel one animated property of one target belongs to.</summary>
    public static CompositorChannel Of(AdamantiumComponent target, AdamantiumProperty property)
    {
        if (target is Transform) return CompositorChannel.Transform;

        // An element's own Opacity stays on the loop: composited, the property would stop advancing for bindings and
        // triggers, and a fade already dirties just one element.
        if (target is IUIComponent && ReferenceEquals(property, OpacityOf(target))) return CompositorChannel.None;

        var metadata = property.GetDefaultMetadata(target.GetType());
        return metadata is { AffectsPaint: true } ? CompositorChannel.Paint : CompositorChannel.None;
    }

    // The Opacity property as REGISTERED for this target's type. Looked up by name once per type through the property
    // system's own registry - the same lookup a binding does - rather than referencing UIComponent, which lives above.
    private static AdamantiumProperty OpacityOf(AdamantiumComponent target)
    {
        if (OpacityByType.TryGetValue(target.GetType(), out var known)) return known;

        AdamantiumProperty found = null;
        foreach (var property in AdamantiumPropertyMap.GetRegistered(target.GetType()))
            if (property.Name == "Opacity") { found = property; break; }

        OpacityByType[target.GetType()] = found;
        return found;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, AdamantiumProperty> OpacityByType = new();

    /// <summary>The channel a whole curve belongs to: every track must land in the SAME non-None channel, or the loop thread
    /// keeps the animation. A curve is one clock over several properties (a scale in X and Y, a wave across three stops) -
    /// splitting it across threads would let its own tracks drift apart, which is worse than not compositing it at all.</summary>
    public static CompositorChannel Of(AdamantiumComponent target, AnimationCurve curve)
    {
        if (curve.Tracks.Length == 0) return CompositorChannel.None;

        var channel = Of(target, curve.Tracks[0].Property);
        if (channel == CompositorChannel.None) return CompositorChannel.None;

        for (var i = 1; i < curve.Tracks.Length; i++)
            if (Of(target, curve.Tracks[i].Property) != channel)
                return CompositorChannel.None;

        return channel;
    }
}
