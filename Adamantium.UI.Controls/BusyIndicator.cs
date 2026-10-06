using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>An indeterminate activity indicator with only <see cref="IsActive"/>. Each look is a theme template chosen by
/// class, which starts and stops its own animation off IsActive.</summary>
public class BusyIndicator : TemplatedUIComponent
{
    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.Register(nameof(IsActive),
        typeof(bool), typeof(BusyIndicator), new PropertyMetadata(true, PropertyMetadataOptions.AffectsRender));

    /// <summary>Whether the indicator is running. False stops its animation and (per the theme) hides it.</summary>
    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BusyIndicatorAutomationPeer(this);
}
