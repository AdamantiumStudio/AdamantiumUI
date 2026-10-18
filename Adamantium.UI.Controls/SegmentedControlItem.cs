using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>One segment of a <see cref="SegmentedControl"/>: written in its markup, or the container it makes for an item.
/// A press picks it; the control, not the segment, takes the keyboard.</summary>
public class SegmentedControlItem : ContentControl, ISelectable
{
    public static readonly AdamantiumProperty IsSelectedProperty = AdamantiumProperty.Register(nameof(IsSelected),
        typeof(bool), typeof(SegmentedControlItem), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender,
            (a, e) => AutomationEvents.RaiseSelected((SegmentedControlItem)a, (bool)e.NewValue)));

    /// <summary>What choosing this segment runs, given <see cref="CommandParameter"/>; while it cannot run, the segment is
    /// disabled. Bound per item through the control's item container style when the segments are made from items.</summary>
    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(SegmentedControlItem), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(SegmentedControlItem),
        new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly AdamantiumProperty IsFirstProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsFirst),
        typeof(bool), typeof(SegmentedControlItem), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BackgroundPointerOverProperty = AdamantiumProperty.Register(
        nameof(BackgroundPointerOver), typeof(Brush), typeof(SegmentedControlItem), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty BackgroundSelectedProperty = AdamantiumProperty.Register(
        nameof(BackgroundSelected), typeof(Brush), typeof(SegmentedControlItem), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty ForegroundSelectedProperty = AdamantiumProperty.Register(
        nameof(ForegroundSelected), typeof(Brush), typeof(SegmentedControlItem), new PropertyMetadata(default(Brush)));

    public bool IsSelected
    {
        get => GetValue<bool>(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private WeakCanExecuteChangedRelay<SegmentedControlItem> _commandRelay;

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not SegmentedControlItem segment)
        {
            return;
        }

        segment._commandRelay?.Detach();
        segment._commandRelay = e.NewValue is ICommand command
            ? new WeakCanExecuteChangedRelay<SegmentedControlItem>(command, segment, static s => s.UpdateCanExecute())
            : null;
        segment.UpdateCanExecute();
    }

    private static void OnCommandParameterChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        (a as SegmentedControlItem)?.UpdateCanExecute();

    private void UpdateCanExecute() => SetCurrentValue(IsEnabledProperty, Command?.CanExecute(CommandParameter) ?? true);

    internal void RunCommand()
    {
        if (Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    /// <summary>Whether it is the first segment, which has no divider before it.</summary>
    public bool IsFirst
    {
        get => GetValue<bool>(IsFirstProperty);
        internal set => SetValue(IsFirstProperty, value);
    }

    public Brush BackgroundPointerOver
    {
        get => GetValue<Brush>(BackgroundPointerOverProperty);
        set => SetValue(BackgroundPointerOverProperty, value);
    }

    public Brush BackgroundSelected
    {
        get => GetValue<Brush>(BackgroundSelectedProperty);
        set => SetValue(BackgroundSelectedProperty, value);
    }

    public Brush ForegroundSelected
    {
        get => GetValue<Brush>(ForegroundSelectedProperty);
        set => SetValue(ForegroundSelectedProperty, value);
    }

    internal SegmentedControl Owner { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || e.Handled || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        e.Handled = true;
        Owner?.PickFromContainer(this);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SegmentedControlItemAutomationPeer(this);
}
