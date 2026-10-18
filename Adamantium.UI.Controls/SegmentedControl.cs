using System;
using System.Collections.Specialized;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// A row of joined segments, one of them chosen - a few choices of one setting that all stay in sight, where a drop-down
/// would hide them. The segments are <see cref="SegmentedControlItem"/>s written in its markup, or made for the items of
/// its <see cref="ItemsControl.ItemsSource"/> - view-models drawn by its item template, each segment's command bound
/// through the item container style. Choosing another segment - a press, the arrows, Home and End, automation - makes it
/// the selection and runs its command; the keys pass disabled segments by. The control is one stop for the keyboard;
/// made not focusable, as in a toolbar, a press leaves the keyboard where it was.
/// </summary>
public class SegmentedControl : Selector
{
    /// <summary>Fills the segments with an enum's values, as <see cref="DropDown.EnumType"/> does; the chosen value is the
    /// selected item. Each shows its name unless an item template says otherwise.</summary>
    public static readonly AdamantiumProperty EnumTypeProperty = AdamantiumProperty.Register(nameof(EnumType),
        typeof(Type), typeof(SegmentedControl), new PropertyMetadata(null, OnEnumTypeChanged));

    public Type EnumType
    {
        get => GetValue<Type>(EnumTypeProperty);
        set => SetValue(EnumTypeProperty, value);
    }

    private static void OnEnumTypeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is SegmentedControl segments && e.NewValue is Type { IsEnum: true } type)
        {
            segments.ItemsSource = Enum.GetValues(type);
        }
    }

    static SegmentedControl()
    {
        FocusableProperty.OverrideMetadata(typeof(SegmentedControl), new PropertyMetadata(true));
    }

    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var segment = new SegmentedControlItem { Owner = this };
        if (ItemContainerStyle != null)
        {
            segment.Styles.Add(ItemContainerStyle);
        }

        return segment;
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        base.PrepareContainer(container, item);
        if (container is not SegmentedControlItem segment)
        {
            return;
        }

        if (!ReferenceEquals(segment, item))
        {
            segment.DataContext = item;
            segment.ContentTemplate = ItemTemplate;
            segment.ContentTemplateSelector = ItemTemplateSelector;
            segment.Content = item;
        }

        segment.Owner = this;
        segment.IsSelected = IsItemSelected(item);
        segment.IsFirst = IndexOfItem(item) == 0;
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is SegmentedControlItem segment)
        {
            segment.IsSelected = false;
            if (!ReferenceEquals(segment, ItemFor(segment)))
            {
                segment.DataContext = null;
            }

            return;
        }

        base.ClearContainer(container);
    }

    internal void PickFromContainer(SegmentedControlItem segment)
    {
        var index = IndexOfItem(ItemFor(segment));
        if (index < 0 || !IsEnabled || !segment.IsEnabled)
        {
            return;
        }

        if (Focusable)
        {
            Focus();
        }

        Choose(index);
    }

    private void Choose(int index)
    {
        if (index == SelectedIndex)
        {
            return;
        }

        SelectSingle(index);
        SegmentAt(index)?.RunCommand();
    }

    private SegmentedControlItem SegmentAt(int index) =>
        Items[index] as SegmentedControlItem ?? ItemContainerGenerator?.ContainerFromIndex(index) as SegmentedControlItem;

    private bool CanLandOn(int index) => SegmentAt(index) is not { IsEnabled: false };

    private int EnabledFrom(int start, int step)
    {
        for (var i = start; i >= 0 && i < Items.Count; i += step)
        {
            if (CanLandOn(i))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Items.Count == 0 || !IsEnabled)
        {
            return;
        }

        var index = SelectedIndex;
        var next = e.Key switch
        {
            Key.LeftArrow => EnabledFrom(index < 0 ? Items.Count - 1 : index - 1, -1),
            Key.RightArrow => EnabledFrom(index + 1, 1),
            Key.Home => EnabledFrom(0, 1),
            Key.End => EnabledFrom(Items.Count - 1, -1),
            _ => -2
        };

        if (next == -2)
        {
            return;
        }

        e.Handled = true;
        if (next >= 0)
        {
            Choose(next);
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);

        var pending = TakePendingSelectionIndex();
        var selected = pending >= 0 ? pending : IndexOfItem(SelectedItem);
        if (selected < 0 && SelectedItem != null && !string.IsNullOrEmpty(SelectedValuePath))
        {
            selected = IndexOfValue(SelectedValue);
        }

        if (selected != SelectedIndex)
        {
            SelectSingle(selected);
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is SegmentedControlItem written)
            {
                written.IsFirst = i == 0;
            }
        }

        foreach (var (container, item) in RealizedContainers())
        {
            if (container is SegmentedControlItem made && IndexOfItem(made) < 0)
            {
                made.IsFirst = IndexOfItem(item) == 0;
            }
        }
    }

    private object ItemFor(SegmentedControlItem segment)
    {
        var index = ItemContainerGenerator?.IndexFromContainer(segment) ?? -1;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SegmentedControlAutomationPeer(this);
}
