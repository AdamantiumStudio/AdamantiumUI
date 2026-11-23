using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

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

    /// <summary>Whether the section is where a shown context comes from - the picture put in from here, now selected,
    /// its contextual bar shown - so it is framed and washed in the context's <see cref="Accent"/>, and read as kin of
    /// that bar. In the overflow menu, its bar's chevron is marked so (<see cref="ToolBar.HasContextInOverflow"/>).</summary>
    public static readonly AdamantiumProperty IsInContextProperty = AdamantiumProperty.Register(nameof(IsInContext),
        typeof(bool), typeof(ToolBarGroup), new PropertyMetadata(false, OnContextChanged));

    /// <summary>The color of the context the section is in: its contextual bar's <see cref="ToolBar.Accent"/>. The
    /// theme's accent unless set.</summary>
    public static readonly AdamantiumProperty AccentProperty = AdamantiumProperty.Register(nameof(Accent),
        typeof(Brush), typeof(ToolBarGroup), new PropertyMetadata(default(Brush), OnContextChanged));

    public bool IsInContext
    {
        get => GetValue<bool>(IsInContextProperty);
        set => SetValue(IsInContextProperty, value);
    }

    public Brush Accent
    {
        get => GetValue<Brush>(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>The bar the section is a command of, set by it, so it hears when the section's context changes.</summary>
    internal ToolBar Bar { get; set; }

    private bool _isInOverflow;
    private readonly List<ToolBarItem> _items = [];

    /// <summary>Whether the section is in its bar's overflow menu, set by the bar: its commands stand there as they are,
    /// not as on the bar's column.</summary>
    internal bool IsInOverflow
    {
        get => _isInOverflow;
        set
        {
            if (_isInOverflow == value)
            {
                return;
            }

            _isInOverflow = value;
            // At once: a closed menu does not measure what it holds, and a command folded on the column stays folded.
            PlaceItems();
            InvalidateMeasure();
        }
    }

    protected internal override bool IsItemItsOwnContainer(object item) => ToolBarItem.IsOwnContainer(item);

    protected internal override IUIComponent GetContainerForItem(object item) => ToolBarItem.Create(ItemContainerStyle);

    protected internal override void PrepareContainer(IUIComponent container, object item) =>
        ToolBarItem.Prepare(container, item, ItemTemplate);

    // Its commands are told where they stand before they are measured; its panel makes the items of new ones as it
    // measures, so those are told after it, and measured again.
    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        PlaceItems();
        var size = base.MeasureOverride(availableSize);
        return PlaceItems() ? base.MeasureOverride(availableSize) : size;
    }

    private bool PlaceItems()
    {
        _items.Clear();
        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is ToolBarItem item)
            {
                _items.Add(item);
            }
        }

        var bar = Bar;
        return ToolBarItem.Place(_items, bar != null && Orientation == Orientation.Vertical && !IsInOverflow,
            bar?.ColumnWidth ?? 0);
    }

    private static void OnContextChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((ToolBarGroup)a).Bar?.MarkContextInOverflow();

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
