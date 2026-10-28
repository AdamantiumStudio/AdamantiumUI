using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>Which way the content opens, and therefore where the header sits.</summary>
public enum ExpandDirection
{
    /// <summary>Header on top, content below it - what a section of a form or an inspector is.</summary>
    Down,

    /// <summary>Header at the bottom, content above it - a drawer rising from an edge.</summary>
    Up,

    /// <summary>Header on the right, content to its left.</summary>
    Left,

    /// <summary>Header on the left, content to its right - a side rail that opens sideways.</summary>
    Right
}

/// <summary>A header with content that folds away under it; collapsed content is not measured. Templated: PART_Header
/// toggles, PART_Content holds the content. A template may unfold the content with a <see cref="RevealDecorator"/>
/// named PART_Reveal and turn its chevron with a <see cref="Transform"/> named PART_GlyphTurn, over
/// <see cref="ExpandDuration"/>.</summary>
public class Expander : ContentControl
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(Expander), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty HeaderTemplateProperty = AdamantiumProperty.Register(
        nameof(HeaderTemplate), typeof(DataTemplate), typeof(Expander),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty IsExpandedProperty = AdamantiumProperty.Register(nameof(IsExpanded),
        typeof(bool), typeof(Expander),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault | PropertyMetadataOptions.AffectsMeasure,
            OnIsExpandedChanged));

    public static readonly AdamantiumProperty ExpandDirectionProperty = AdamantiumProperty.Register(
        nameof(ExpandDirection), typeof(ExpandDirection), typeof(Expander),
        new PropertyMetadata(ExpandDirection.Down, PropertyMetadataOptions.AffectsMeasure, OnExpandDirectionChanged));

    public static readonly AdamantiumProperty ExpandDurationProperty = AdamantiumProperty.Register(
        nameof(ExpandDuration), typeof(TimeSpan), typeof(Expander), new PropertyMetadata(TimeSpan.Zero));

    /// <summary>Whether the header can be folded at all. A section that must stay open still wants its header.</summary>
    public static readonly AdamantiumProperty CanCollapseProperty = AdamantiumProperty.Register(nameof(CanCollapse),
        typeof(bool), typeof(Expander), new PropertyMetadata(true));

    private static readonly IEasingFunction ExpandEasing = new CubicEasing { Mode = EasingMode.Out };

    private IInputComponent _header;
    private RevealDecorator _reveal;
    private Transform _glyphTurn;
    private bool _arranged;

    static Expander()
    {
        FocusableProperty.OverrideMetadata(typeof(Expander), new PropertyMetadata(true));
    }

    /// <summary>What the header shows. A string, or anything <see cref="HeaderTemplate"/> knows how to draw.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public DataTemplate HeaderTemplate
    {
        get => GetValue<DataTemplate>(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>Open or folded. Two-way bindable: a view-model that remembers which sections its user left open is the
    /// ordinary case, and the control must not be the one holding that memory.</summary>
    public bool IsExpanded
    {
        get => GetValue<bool>(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public ExpandDirection ExpandDirection
    {
        get => GetValue<ExpandDirection>(ExpandDirectionProperty);
        set => SetValue(ExpandDirectionProperty, value);
    }

    public bool CanCollapse
    {
        get => GetValue<bool>(CanCollapseProperty);
        set => SetValue(CanCollapseProperty, value);
    }

    /// <summary>How long the content takes to unfold or fold away, and the chevron to turn; zero shows the change at once.
    /// </summary>
    public TimeSpan ExpandDuration
    {
        get => GetValue<TimeSpan>(ExpandDurationProperty);
        set => SetValue(ExpandDurationProperty, value);
    }

    /// <summary>Raised after the content opens.</summary>
    public event EventHandler Expanded;

    /// <summary>Raised after the content folds away.</summary>
    public event EventHandler Collapsed;

    /// <summary>Flips <see cref="IsExpanded"/>, unless <see cref="CanCollapse"/> says the section stays open. True when
    /// the state actually changed.</summary>
    public bool Toggle()
    {
        if (!CanCollapse && IsExpanded) return false;

        // SetCurrentValue, NOT the CLR setter: a user's click must not write a LOCAL value, which outranks a binding
        // and would leave the expander deaf to its own source ever after - which folding state bound to a view-model
        // is exactly the case for. Mirrors ToggleButton.OnToggle and Slider.
        SetCurrentValue(IsExpandedProperty, !IsExpanded);
        return true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_header != null) _header.PointerPressed -= OnHeaderPressed;
        _header = GetTemplateChild("PART_Header") as IInputComponent;
        if (_header != null) _header.PointerPressed += OnHeaderPressed;
        if (_reveal != null) _reveal.PropertyChanged -= OnRevealPropertyChanged;
        _reveal = GetTemplateChild("PART_Reveal") as RevealDecorator;
        if (_reveal != null) _reveal.PropertyChanged += OnRevealPropertyChanged;
        _glyphTurn = GetTemplateChild("PART_GlyphTurn") as Transform;
        ShowState(animate: false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowState(animate: false);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        _arranged = true;
        return base.ArrangeOverride(finalSize);
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();

        if (_header != null) _header.PointerPressed -= OnHeaderPressed;
        _header = null;
        if (_reveal != null) _reveal.PropertyChanged -= OnRevealPropertyChanged;
        _reveal = null;
        _glyphTurn = null;
    }

    /// <summary>Space and Enter fold the section, as they do on any header that is a button in all but name.
    /// <para>Only when the section ITSELF has the keyboard. A key event travels up from wherever it happened, so this
    /// used to hear every space typed into every field inside the section - and folded the section away in the middle
    /// of a word. What is inside an expander belongs to what is inside it.</para></summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled) return;
        if (!ReferenceEquals(e.OriginalSource, this)) return;

        if (e.Key is Key.Space or Key.Enter) e.Handled = Toggle();
    }

    private static void OnIsExpandedChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not Expander expander) return;

        expander.ShowState(animate: true);
        if (expander.IsExpanded) expander.Expanded?.Invoke(expander, EventArgs.Empty);
        else expander.Collapsed?.Invoke(expander, EventArgs.Empty);
        AutomationEvents.RaiseExpandCollapse(expander, expander.IsExpanded);
    }

    private static void OnExpandDirectionChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        ((Expander)d).ShowState(animate: false);
    }

    private void ShowState(bool animate)
    {
        var open = IsExpanded;
        var duration = ExpandDuration;
        animate &= duration > TimeSpan.Zero && IsAttachedToVisualTree && _arranged;

        if (_reveal is { } reveal)
        {
            var progress = open ? 1.0 : 0.0;
            if (open)
            {
                reveal.Visibility = Visibility.Visible;
            }

            if (animate)
            {
                reveal.BeginAnimation(RevealDecorator.ProgressProperty,
                    new DoubleAnimation { From = reveal.Progress, To = progress, Duration = duration, Easing = ExpandEasing },
                    () => FoldAway(reveal));
            }
            else
            {
                reveal.CancelAnimation(RevealDecorator.ProgressProperty);
                reveal.Progress = progress;
                FoldAway(reveal);
            }
        }

        if (_glyphTurn is { } turn)
        {
            var angle = GlyphAngle(_reveal?.Direction ?? ExpandDirection, open);
            if (animate)
            {
                turn.BeginAnimation(Transform.RotationAngleProperty,
                    new DoubleAnimation { From = turn.RotationAngle, To = angle, Duration = duration, Easing = ExpandEasing });
            }
            else
            {
                turn.CancelAnimation(Transform.RotationAngleProperty);
                turn.RotationAngle = angle;
            }
        }
    }

    private void OnRevealPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == RevealDecorator.DirectionProperty)
        {
            ShowState(animate: false);
        }
    }

    private void FoldAway(RevealDecorator reveal)
    {
        if (!IsExpanded && ReferenceEquals(reveal, _reveal))
        {
            reveal.Visibility = Visibility.Collapsed;
        }
    }

    private static double GlyphAngle(ExpandDirection direction, bool open) => direction switch
    {
        ExpandDirection.Up => open ? -90 : 0,
        ExpandDirection.Right => open ? 180 : 0,
        ExpandDirection.Left => open ? 0 : 180,
        _ => open ? 90 : 0
    };

    private void OnHeaderPressed(object sender, PointerPressedEventArgs e)
    {
        if (!IsEnabled || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        Focus();
        if (Toggle()) e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ExpanderAutomationPeer(this);
}
