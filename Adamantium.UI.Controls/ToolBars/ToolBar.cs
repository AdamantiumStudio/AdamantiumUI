using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// A bar of commands - any controls, drawn as their theme draws them - in a <see cref="ToolBarTray"/>, which places it by
/// its <see cref="Band"/> and <see cref="BandIndex"/> and moves it when its grip is dragged. When the bar is short of
/// room, its last commands go to its overflow menu, as each command's <see cref="OverflowModeProperty"/> allows.
/// </summary>
public class ToolBar : ItemsControl
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(ToolBar), new PropertyMetadata(null));

    public static readonly AdamantiumProperty BandProperty = AdamantiumProperty.Register(nameof(Band),
        typeof(int), typeof(ToolBar),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsParentMeasure | PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty BandIndexProperty = AdamantiumProperty.Register(nameof(BandIndex),
        typeof(int), typeof(ToolBar),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsParentMeasure | PropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly AdamantiumPropertyKey OrientationPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(Orientation), typeof(Orientation), typeof(ToolBar),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsMeasure,
            static (a, _) => ((ToolBar)a).OnOrientationChanged()));

    public static readonly AdamantiumProperty OrientationProperty = OrientationPropertyKey.Property;

    public static readonly AdamantiumProperty IsOverflowOpenProperty = AdamantiumProperty.Register(nameof(IsOverflowOpen),
        typeof(bool), typeof(ToolBar),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsOverflowOpenChanged));

    private static readonly AdamantiumPropertyKey HasOverflowItemsPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(HasOverflowItems), typeof(bool), typeof(ToolBar), new PropertyMetadata(false));

    public static readonly AdamantiumProperty HasOverflowItemsProperty = HasOverflowItemsPropertyKey.Property;

    private static readonly AdamantiumPropertyKey IsMovingPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(IsMoving), typeof(bool), typeof(ToolBar), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsMovingProperty = IsMovingPropertyKey.Property;

    /// <summary>Whether the bar is shown by what the user is on - a table, a picture - rather than by the user, as a
    /// ribbon's contextual tab is: its tray's menu lists it apart, and does not switch it.</summary>
    public static readonly AdamantiumProperty IsContextualProperty = AdamantiumProperty.Register(nameof(IsContextual),
        typeof(bool), typeof(ToolBar), new PropertyMetadata(false));

    /// <summary>How long the overflow button is along the bar - a theme metric, reserved whenever commands overflow.</summary>
    public static readonly AdamantiumProperty OverflowButtonLengthProperty = AdamantiumProperty.Register(
        nameof(OverflowButtonLength), typeof(double), typeof(ToolBar),
        new PropertyMetadata(20.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The room between two commands, on the bar and in its overflow menu - a theme metric.</summary>
    public static readonly AdamantiumProperty SpacingProperty = AdamantiumProperty.Register(nameof(Spacing),
        typeof(double), typeof(ToolBar), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Where a command stands when its bar is short of room; set on the command.</summary>
    public static readonly AdamantiumProperty OverflowModeProperty = AdamantiumProperty.RegisterAttached("OverflowMode",
        typeof(ToolBarOverflowMode), typeof(AdamantiumComponent),
        new PropertyMetadata(ToolBarOverflowMode.AsNeeded, PropertyMetadataOptions.AffectsParentMeasure));

    private readonly List<IMeasurableComponent> _commands = [];
    private readonly List<bool> _overflowing = [];
    private readonly Dictionary<IMeasurableComponent, double> _lengths = new();
    private Panel _onBar;
    private Panel _inOverflow;
    private Thumb _grip;
    private double _chrome = double.NaN;
    private readonly RoutedEventHandler _closeOverflow;
    private ToolBarTray _movedIn;

    public ToolBar()
    {
        _closeOverflow = CloseOverflow;
    }

    static ToolBar()
    {
        // One stop for Tab, the arrows within it: the bar is one control to the keyboard, as in Office and VS.
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(ToolBar),
            new PropertyMetadata(KeyboardNavigationMode.Once));
        KeyboardNavigation.DirectionalNavigationProperty.OverrideMetadata(typeof(ToolBar),
            new PropertyMetadata(KeyboardNavigationMode.Cycle));
    }

    public static ToolBarOverflowMode GetOverflowMode(IAdamantiumComponent element) =>
        element.GetValue<ToolBarOverflowMode>(OverflowModeProperty);

    public static void SetOverflowMode(IAdamantiumComponent element, ToolBarOverflowMode value) =>
        element.SetValue(OverflowModeProperty, value);

    /// <summary>The bar's name: in its tray's menu and to automation.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The row of its tray the bar is in, counted from zero; the tray renumbers the rows after a move.</summary>
    public int Band
    {
        get => GetValue<int>(BandProperty);
        set => SetValue(BandProperty, value);
    }

    /// <summary>Where the bar stands in its row, counted from zero.</summary>
    public int BandIndex
    {
        get => GetValue<int>(BandIndexProperty);
        set => SetValue(BandIndexProperty, value);
    }

    /// <summary>Along which way the bar runs - its tray's.</summary>
    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        internal set => SetValue(OrientationPropertyKey, value);
    }

    /// <summary>Whether the overflow menu is open.</summary>
    public bool IsOverflowOpen
    {
        get => GetValue<bool>(IsOverflowOpenProperty);
        set => SetValue(IsOverflowOpenProperty, value);
    }

    /// <summary>Whether any command is in the overflow menu.</summary>
    public bool HasOverflowItems
    {
        get => GetValue<bool>(HasOverflowItemsProperty);
        private set => SetValue(HasOverflowItemsPropertyKey, value);
    }

    /// <summary>Whether the bar is being dragged by its grip.</summary>
    public bool IsMoving
    {
        get => GetValue<bool>(IsMovingProperty);
        internal set => SetValue(IsMovingPropertyKey, value);
    }

    public bool IsContextual
    {
        get => GetValue<bool>(IsContextualProperty);
        set => SetValue(IsContextualProperty, value);
    }

    public double Spacing
    {
        get => GetValue<double>(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double OverflowButtonLength
    {
        get => GetValue<double>(OverflowButtonLengthProperty);
        set => SetValue(OverflowButtonLengthProperty, value);
    }

    /// <summary>How long the bar is with every command it can show on it.</summary>
    internal double FullLength { get; private set; } = double.NaN;

    /// <summary>How short the bar can be: its grip, the commands that never overflow and the overflow button.</summary>
    internal double ShortestLength { get; private set; } = double.NaN;

    internal ToolBarTray Tray => this.GetVisualAncestors().OfType<ToolBarTray>().FirstOrDefault();

    /// <summary>Whether the bar is measuring its commands itself: its panel, measured then, has nothing to tell it.</summary>
    internal bool IsMeasuringCommands { get; private set; }

    public override Panel ItemsHostPanel => _onBar;

    public override void OnApplyTemplate()
    {
        TakeCommandsOut();
        base.OnApplyTemplate();
        _onBar = GetTemplateChild("PART_Items") as Panel;
        HookOverflow(GetTemplateChild("PART_OverflowItems") as Panel);
        HookGrip(GetTemplateChild("PART_Grip") as Thumb);

        // The menu of what does not fit opens under its chevron.
        if (GetTemplateChild("PART_OverflowPopup") is Popup popup && popup.PlacementTarget == null &&
            GetTemplateChild("PART_OverflowButton") is UIComponent chevron)
        {
            popup.PlacementTarget = chevron;
        }
        _chrome = double.NaN;
        Realize();
    }

    public override void OnRemoveTemplate()
    {
        TakeCommandsOut();
        HookGrip(null);
        HookOverflow(null);
        _onBar = null;
        base.OnRemoveTemplate();
    }

    // A command run from the overflow menu puts the menu away, as a menu's does.
    private void HookOverflow(Panel overflow)
    {
        _inOverflow?.RemoveHandler(ButtonBase.ClickEvent, _closeOverflow);
        _inOverflow = overflow;
        _inOverflow?.AddHandler(ButtonBase.ClickEvent, _closeOverflow);
    }

    private void CloseOverflow(object sender, RoutedEventArgs e) => SetCurrentValue(IsOverflowOpenProperty, false);

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        Realize();
    }

    protected override void OnItemTemplateChangedCore() => Realize();

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolBarAutomationPeer(this);

    // The commands are this bar's to share out between the bar and the overflow menu, so no presenter fills a panel.
    private void Realize()
    {
        TakeCommandsOut();
        ItemContainerGenerator.Clear();
        _commands.Clear();
        _overflowing.Clear();
        if (_onBar == null)
        {
            return;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.Realize(i) is IMeasurableComponent command)
            {
                if (command is ToolBarGroup group)
                {
                    group.Orientation = Orientation;
                }

                _commands.Add(command);
                _overflowing.Add(false);
                _onBar.Children.Add(command);
            }
        }

        MarkSeparators();
        InvalidateMeasure();
    }

    // Turned, the bar measures its commands again along the new way, its sections turned with it.
    private void OnOrientationChanged()
    {
        _lengths.Clear();
        foreach (var group in _commands.OfType<ToolBarGroup>())
        {
            group.Orientation = Orientation;
        }
    }

    // A section's rule stands where a shown command stands before it, in the same place - the bar or the menu - so the
    // first thing on the bar, or in the menu, has none.
    private void MarkSeparators()
    {
        var shownOnBar = false;
        var shownInOverflow = false;
        for (var i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            var before = _overflowing[i] ? shownInOverflow : shownOnBar;
            if (command is ToolBarGroup group)
            {
                group.ShowsSeparator = before;
            }

            if (command.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (_overflowing[i])
            {
                shownInOverflow = true;
            }
            else
            {
                shownOnBar = true;
            }
        }
    }

    private void TakeCommandsOut()
    {
        _onBar?.Children.Clear();
        _inOverflow?.Children.Clear();
    }

    private void HookGrip(Thumb grip)
    {
        if (_grip != null)
        {
            _grip.DragStarted -= OnGripDragStarted;
            _grip.DragDelta -= OnGripDragDelta;
            _grip.DragCompleted -= OnGripDragCompleted;
        }

        _grip = grip;
        if (_grip != null)
        {
            _grip.DragStarted += OnGripDragStarted;
            _grip.DragDelta += OnGripDragDelta;
            _grip.DragCompleted += OnGripDragCompleted;
        }
    }

    // The tray the bar is moved in is found once, when the grip is pressed.
    private void OnGripDragStarted(object sender, DragStartedEventArgs e)
    {
        e.Handled = true;
        _movedIn = Tray;
        _movedIn?.StartMoving(this, e.Offset);
    }

    private void OnGripDragDelta(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _movedIn?.Move(this, e.Change);
    }

    private void OnGripDragCompleted(object sender, DragCompletedEventArgs e)
    {
        e.Handled = true;
        var tray = _movedIn;
        _movedIn = null;
        tray?.FinishMoving(this, e.IsCanceled);
    }

    // The commands that do not fit go to the overflow menu, the last first. What decides it is each command's length as
    // last measured on the bar, so the decision does not change what it was made from.
    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        IsMeasuringCommands = true;
        try
        {
            return MeasureCommandsAndBar(availableSize);
        }
        finally
        {
            IsMeasuringCommands = false;
        }
    }

    private Size<double> MeasureCommandsAndBar(Size<double> availableSize)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        if (_onBar == null)
        {
            var bare = base.MeasureOverride(availableSize);
            FullLength = ShortestLength = Along(bare, horizontal) +
                                          (horizontal ? Margin.Left + Margin.Right : Margin.Top + Margin.Bottom);
            return bare;
        }

        var room = horizontal ? availableSize.Width : availableSize.Height;
        var across = horizontal ? availableSize.Height : availableSize.Width;

        // A command shown or hidden since the last pass moves the rules of the sections after it: marked before the
        // sections are measured, so they are measured as they will be drawn.
        MarkSeparators();
        var lengths = MeasureCommands(horizontal, across);

        PlaceCommands(Overflowing(room, lengths));
        var size = base.MeasureOverride(availableSize);
        var chrome = Along(size, horizontal) - Along(_onBar.DesiredSize, horizontal) -
                     (HasOverflowItems ? OverflowButtonLength : 0);
        chrome = Math.Max(0, chrome);
        if (!chrome.Equals(_chrome))
        {
            _chrome = chrome;
            if (PlaceCommands(Overflowing(room, lengths)))
            {
                size = base.MeasureOverride(availableSize);
            }
        }

        // In the tray's terms: with the bar's margin, as the length it is given is.
        var margin = horizontal ? Margin.Left + Margin.Right : Margin.Top + Margin.Bottom;
        FullLength = margin + _chrome + OnBar(lengths, Overflowing(mode => mode == ToolBarOverflowMode.Always)) +
                     (Any(ToolBarOverflowMode.Always) ? OverflowButtonLength : 0);
        ShortestLength = margin + _chrome + OnBar(lengths, Overflowing(mode => mode != ToolBarOverflowMode.Never)) +
                         (_commands.Count > 0 && !All(ToolBarOverflowMode.Never) ? OverflowButtonLength : 0);
        return size;
    }

    private static double Along(Size<double> size, bool horizontal) => horizontal ? size.Width : size.Height;

    private double[] MeasureCommands(bool horizontal, double across)
    {
        var lengths = new double[_commands.Count];
        for (var i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            if (!_overflowing[i] || !_lengths.TryGetValue(command, out var length))
            {
                command.Measure(horizontal
                    ? new Size<double>(double.PositiveInfinity, across)
                    : new Size<double>(across, double.PositiveInfinity));
                length = Along(command.DesiredSize, horizontal);
                _lengths[command] = length;
            }

            lengths[i] = length;
        }

        if (_lengths.Count > _commands.Count)
        {
            foreach (var gone in _lengths.Keys.Where(command => !_commands.Contains(command)).ToList())
            {
                _lengths.Remove(gone);
            }
        }

        return lengths;
    }

    private bool[] Overflowing(double room, double[] lengths)
    {
        var chrome = double.IsNaN(_chrome) ? 0 : _chrome;
        var overflowing = Overflowing(mode => mode == ToolBarOverflowMode.Always);
        if (chrome + OnBar(lengths, overflowing) + (overflowing.Contains(true) ? OverflowButtonLength : 0) <=
            room + Tolerance)
        {
            return overflowing;
        }

        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            if (chrome + OverflowButtonLength + OnBar(lengths, overflowing) <= room + Tolerance)
            {
                break;
            }

            if (!overflowing[i] && GetOverflowMode(_commands[i]) == ToolBarOverflowMode.AsNeeded)
            {
                overflowing[i] = true;
            }
        }

        return overflowing;
    }

    private bool[] Overflowing(System.Func<ToolBarOverflowMode, bool> goes)
    {
        var overflowing = new bool[_commands.Count];
        for (var i = 0; i < _commands.Count; i++)
        {
            overflowing[i] = goes(GetOverflowMode(_commands[i]));
        }

        return overflowing;
    }

    // How long the commands left on the bar are, with the spacing between those shown, as the bar's panel lays them out.
    private double OnBar(double[] lengths, bool[] overflowing)
    {
        var length = 0.0;
        var shown = 0;
        for (var i = 0; i < lengths.Length; i++)
        {
            if (!overflowing[i] && _commands[i].Visibility != Visibility.Collapsed)
            {
                length += lengths[i];
                shown++;
            }
        }

        return length + Math.Max(0, Spacing) * Math.Max(0, shown - 1);
    }

    private const double Tolerance = 1e-6;

    // Moved only when the share changes: a move takes the commands out of the tree and puts them back.
    private bool PlaceCommands(bool[] overflowing)
    {
        var changed = false;
        for (var i = 0; i < overflowing.Length && !changed; i++)
        {
            changed = overflowing[i] != _overflowing[i];
        }

        if (changed)
        {
            TakeCommandsOut();
            for (var i = 0; i < overflowing.Length; i++)
            {
                _overflowing[i] = overflowing[i];
                (overflowing[i] ? _inOverflow : _onBar)?.Children.Add(_commands[i]);
            }

            MarkSeparators();
        }

        HasOverflowItems = overflowing.Contains(true);
        if (!HasOverflowItems)
        {
            SetCurrentValue(IsOverflowOpenProperty, false);
        }

        return changed;
    }

    private bool Any(ToolBarOverflowMode mode) => _commands.Any(command => GetOverflowMode(command) == mode);

    private bool All(ToolBarOverflowMode mode) => _commands.All(command => GetOverflowMode(command) == mode);

    private static void OnIsOverflowOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ToolBar { HasOverflowItems: false } bar && Equals(e.NewValue, true))
        {
            bar.SetCurrentValue(IsOverflowOpenProperty, false);
        }
    }
}
