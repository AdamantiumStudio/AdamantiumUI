using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls;

/// <summary>The name of a contextual group, standing in the tab row just before its run of tabs; placed by
/// <see cref="Panels.RibbonTabPanel"/>. In the row, not over it, so a context appearing never changes the strip's
/// height. Only a group that shows its header has one (<see cref="RibbonContextualGroup.ShowContextualHeader"/>).</summary>
public class RibbonContextualLabel : ContentControl
{
    /// <summary>The group's color. Its own property rather than Background, so the theme decides how the color is
    /// used - a solid plate, a rule, a wash.</summary>
    public static readonly AdamantiumProperty AccentProperty = AdamantiumProperty.Register(nameof(Accent),
        typeof(Brush), typeof(RibbonContextualLabel), new PropertyMetadata(default(Brush), PropertyMetadataOptions.AffectsRender));

    public Brush Accent
    {
        get => GetValue<Brush>(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    static RibbonContextualLabel()
    {
        // A label, not a target: it says what a run of tabs is, and clicking it would mean nothing.
        FocusableProperty.OverrideMetadata(typeof(RibbonContextualLabel), new PropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(RibbonContextualLabel), new PropertyMetadata(false));
    }

    /// <summary>None: the label is the name of its tabs, and the tabs carry their context.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => null;
}
