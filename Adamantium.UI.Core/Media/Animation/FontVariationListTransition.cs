namespace Adamantium.UI.Core.Media.Animation;

/// <summary>A <see cref="Transition"/> for <c>FontVariations</c>, CSS's transition of <c>font-variation-settings</c>:
/// a change of the axis values moves the text from the values it shows to the new ones
/// (<see cref="FontVariationListAnimation"/>).</summary>
public sealed class FontVariationListTransition : Transition
{
    internal override bool TryApply(AnimatableUIComponent target, AdamantiumProperty property, object oldValue, object newValue)
    {
        if (newValue is not FontVariationList to || oldValue is not FontVariationList shown)
        {
            return false;
        }

        var from = new FontVariationList(shown);
        if (from.Equals(to))
        {
            return false;
        }

        target.BeginAnimation(property, new FontVariationListAnimation { From = from, To = to, Duration = Duration, Easing = Easing });
        return true;
    }
}
