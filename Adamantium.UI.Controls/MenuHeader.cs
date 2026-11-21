using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>The name of a section of a menu, over its rows: not a command, so it takes no focus and nothing runs it, as a
/// <see cref="Separator"/> does not. The theme draws it as a quiet caption.</summary>
public class MenuHeader : Control
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(MenuHeader), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    static MenuHeader()
    {
        FocusableProperty.OverrideMetadata(typeof(MenuHeader), new PropertyMetadata(false));
    }

    /// <summary>The section's name.</summary>
    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MenuHeaderAutomationPeer(this);
}
