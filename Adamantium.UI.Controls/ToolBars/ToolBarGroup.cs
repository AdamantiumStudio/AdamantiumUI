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

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolBarGroupAutomationPeer(this);
}
