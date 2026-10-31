using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A small mark that says "there is something here": a count, or a dot with no number. Stands on its own and is
/// placed by its parent, typically over the corner of an icon or a button.</summary>
public class Badge : Control
{
    /// <summary>The number shown.</summary>
    public static readonly AdamantiumProperty CountProperty = AdamantiumProperty.Register(nameof(Count),
        typeof(int), typeof(Badge), new PropertyMetadata(0, OnShownChanged));

    /// <summary>The largest number shown as is; a greater count reads "<c>MaxCount</c>+".</summary>
    public static readonly AdamantiumProperty MaxCountProperty = AdamantiumProperty.Register(nameof(MaxCount),
        typeof(int), typeof(Badge), new PropertyMetadata(99, OnShownChanged));

    /// <summary>A dot with no number: "something new", when how much does not matter.</summary>
    public static readonly AdamantiumProperty IsDotProperty = AdamantiumProperty.Register(nameof(IsDot),
        typeof(bool), typeof(Badge), new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>What the badge says: the count, capped by <see cref="MaxCount"/>.</summary>
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.RegisterReadOnly(nameof(Text),
        typeof(string), typeof(Badge), new PropertyMetadata("0", PropertyMetadataOptions.AffectsMeasure));

    public int Count
    {
        get => GetValue<int>(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public int MaxCount
    {
        get => GetValue<int>(MaxCountProperty);
        set => SetValue(MaxCountProperty, value);
    }

    public bool IsDot
    {
        get => GetValue<bool>(IsDotProperty);
        set => SetValue(IsDotProperty, value);
    }

    public string Text
    {
        get => GetValue<string>(TextProperty);
        private set => SetValue(TextProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BadgeAutomationPeer(this);

    private static void OnShownChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not Badge badge)
        {
            return;
        }

        badge.Text = badge.Count > badge.MaxCount ? badge.MaxCount + "+" : badge.Count.ToString();
    }
}
