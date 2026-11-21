using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>
/// A section of a <see cref="ToolBar"/>: commands that belong together, as a ribbon's group does. It is one item of its
/// bar, so when the bar is short of room it goes to the overflow menu whole, never split between the two. The bar draws a
/// rule before it whenever a command stands before it, on the bar or in the menu.
/// </summary>
public class ToolBarGroup : ItemsControl
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(ToolBarGroup), new PropertyMetadata(null));

    private static readonly AdamantiumPropertyKey OrientationPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(Orientation), typeof(Orientation), typeof(ToolBarGroup),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty OrientationProperty = OrientationPropertyKey.Property;

    private static readonly AdamantiumPropertyKey ShowsSeparatorPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(ShowsSeparator), typeof(bool), typeof(ToolBarGroup),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ShowsSeparatorProperty = ShowsSeparatorPropertyKey.Property;

    // No AffectsMeasure: the rule keeps its room either way, so beginning a line changes how the section looks, not where
    // the lines break.
    private static readonly AdamantiumPropertyKey BeginsLinePropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(BeginsLine), typeof(bool), typeof(ToolBarGroup), new PropertyMetadata(false));

    public static readonly AdamantiumProperty BeginsLineProperty = BeginsLinePropertyKey.Property;

    /// <summary>The section's name, to automation.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Along which way its commands run - its bar's.</summary>
    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        internal set => SetValue(OrientationPropertyKey, value);
    }

    /// <summary>Whether a rule stands before it: a command stands before it where it is, on its bar or in the menu.</summary>
    public bool ShowsSeparator
    {
        get => GetValue<bool>(ShowsSeparatorProperty);
        internal set => SetValue(ShowsSeparatorPropertyKey, value);
    }

    /// <summary>Whether it begins a line of a bar whose commands wrap - not the first line: its rule is hidden there and
    /// keeps its room, so hiding it cannot let the section back onto the line before.</summary>
    public bool BeginsLine
    {
        get => GetValue<bool>(BeginsLineProperty);
        internal set => SetValue(BeginsLinePropertyKey, value);
    }

    /// <summary>How much of the section along its bar its rule takes, margin included: a section beginning a line stands
    /// that much back, its hidden rule past the line's start, so its commands line up with the line above.</summary>
    internal double RuleLength =>
        GetTemplateChild("PART_Separator") is IMeasurableComponent rule
            ? Orientation == Orientation.Horizontal ? rule.DesiredSize.Width : rule.DesiredSize.Height
            : 0;

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolBarGroupAutomationPeer(this);
}
