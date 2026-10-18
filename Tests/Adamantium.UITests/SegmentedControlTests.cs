using System.Collections.Generic;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A segmented control is a row of choices, one chosen: a press picks a segment, the arrows move the choice, an
/// enum fills it, and only the first segment goes without a divider.</summary>
[TestFixture]
public class SegmentedControlTests
{
    private enum Shading
    {
        Shaded,
        Wireframe,
        Both
    }

    private static SegmentedControl Written(params string[] labels)
    {
        var control = new SegmentedControl();
        foreach (var label in labels) control.Items.Add(new SegmentedControlItem { Content = label });
        return control;
    }

    private static SegmentedControlItem Segment(SegmentedControl control, int index) =>
        (SegmentedControlItem)control.ItemContainerGenerator.Realize(index);

    private static void Press(SegmentedControl control, Key key) =>
        control.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

    [Test]
    public void PickingASegment_MakesItTheOneChosen()
    {
        var control = Written("Day", "Week", "Month");
        var week = Segment(control, 1);
        var day = Segment(control, 0);

        control.PickFromContainer(week);

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedIndex, Is.EqualTo(1));
            Assert.That(week.IsSelected, Is.True);
            Assert.That(day.IsSelected, Is.False);
        });
    }

    [Test]
    public void TheArrowsHomeAndEnd_MoveTheChoice_AndStopAtTheEnds()
    {
        var control = Written("Day", "Week", "Month");
        control.SelectedIndex = 0;

        Press(control, Key.RightArrow);
        var afterRight = control.SelectedIndex;
        Press(control, Key.End);
        var atEnd = control.SelectedIndex;
        Press(control, Key.RightArrow);
        var pastEnd = control.SelectedIndex;
        Press(control, Key.Home);

        Assert.Multiple(() =>
        {
            Assert.That(afterRight, Is.EqualTo(1));
            Assert.That(atEnd, Is.EqualTo(2));
            Assert.That(pastEnd, Is.EqualTo(2), "nothing past the last segment");
            Assert.That(control.SelectedIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void AnEnum_FillsTheSegments_AndTheChoiceIsItsValue()
    {
        var control = new SegmentedControl { EnumType = typeof(Shading) };

        control.PickFromContainer(Segment(control, 2));

        Assert.Multiple(() =>
        {
            Assert.That(control.Items, Has.Count.EqualTo(3));
            Assert.That(control.SelectedItem, Is.EqualTo(Shading.Both));
        });
    }

    [Test]
    public void OnlyTheFirstSegment_GoesWithoutADivider_EvenAfterTheFirstIsTakenAway()
    {
        var control = Written("Day", "Week", "Month");
        var day = Segment(control, 0);
        var week = Segment(control, 1);
        var firstAtStart = day.IsFirst && !week.IsFirst;

        control.Items.RemoveAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(firstAtStart, Is.True);
            Assert.That(week.IsFirst, Is.True, "the new first one");
        });
    }

    [Test]
    public void ChoosingASegment_RunsItsCommand_ByPressAndByKey()
    {
        var ran = new List<object>();
        var control = new SegmentedControl();
        control.Items.Add(new SegmentedControlItem { Content = "Day", Command = new AdamantiumCommand<object>(ran.Add), CommandParameter = "day" });
        control.Items.Add(new SegmentedControlItem { Content = "Week", Command = new AdamantiumCommand<object>(ran.Add), CommandParameter = "week" });

        control.PickFromContainer(Segment(control, 1));
        Press(control, Key.LeftArrow);

        Assert.That(ran, Is.EqualTo(new[] { "week", "day" }));
    }

    [Test]
    public void ASegmentWhoseCommandCannotRun_IsDisabled_AndTheKeysPassItBy()
    {
        var control = new SegmentedControl();
        control.Items.Add(new SegmentedControlItem { Content = "Day" });
        var week = new SegmentedControlItem { Content = "Week", Command = new AdamantiumCommand<object>(_ => { }, _ => false) };
        control.Items.Add(week);
        control.Items.Add(new SegmentedControlItem { Content = "Month" });
        control.SelectedIndex = 0;

        Press(control, Key.RightArrow);
        var forward = control.SelectedIndex;
        Press(control, Key.LeftArrow);
        var back = control.SelectedIndex;
        control.PickFromContainer(week);

        Assert.Multiple(() =>
        {
            Assert.That(week.IsEnabled, Is.False);
            Assert.That(forward, Is.EqualTo(2), "past the disabled one, to the next");
            Assert.That(back, Is.EqualTo(0), "and back past it");
            Assert.That(control.SelectedIndex, Is.EqualTo(0), "a disabled segment is not picked");
        });
    }

    [Test]
    public void ChoosingTheChosenSegmentAgain_RunsNothing()
    {
        var runs = 0;
        var control = new SegmentedControl();
        var day = new SegmentedControlItem { Content = "Day", Command = new AdamantiumCommand<object>(_ => runs++) };
        control.Items.Add(day);
        control.Items.Add(new SegmentedControlItem { Content = "Week" });

        var segment = Segment(control, 0);
        control.PickFromContainer(segment);
        control.PickFromContainer(segment);

        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void ASegmentTakenAway_MovesTheSelectionWithItsItem()
    {
        var modes = new System.Collections.ObjectModel.ObservableCollection<Mode>
        {
            new("Shaded"), new("Wireframe"), new("Both")
        };
        var control = new SegmentedControl { ItemsSource = modes };
        control.SelectedItem = modes[2];

        modes.RemoveAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedIndex, Is.EqualTo(1));
            Assert.That(control.SelectedItem, Is.SameAs(modes[1]));
        });
    }

    [Test]
    public void AnItemNamedBeforeTheItems_IsWhereTheKeysStart()
    {
        var modes = new[] { new Mode("Shaded"), new Mode("Wireframe"), new Mode("Both") };
        var control = new SegmentedControl();
        control.SelectedItem = modes[1];
        control.ItemsSource = modes;

        Press(control, Key.RightArrow);

        Assert.That(control.SelectedItem, Is.SameAs(modes[2]));
    }

    private sealed record Mode(string Name);

    [Test]
    public void SegmentsMadeFromViewModels_StandForThem()
    {
        var modes = new[] { new Mode("Shaded"), new Mode("Wireframe") };
        var control = new SegmentedControl { ItemsSource = modes };

        var made = Segment(control, 1);
        control.PickFromContainer(made);

        Assert.Multiple(() =>
        {
            Assert.That(made.DataContext, Is.SameAs(modes[1]), "what its template and its command binding read");
            Assert.That(control.SelectedItem, Is.SameAs(modes[1]));
            Assert.That(Segment(control, 0).IsFirst, Is.True);
        });
    }

    private sealed record Choice(Shading Value, string Icon);

    private static Choice[] Choices() => [new(Shading.Shaded, "fill"), new(Shading.Wireframe, "wire"), new(Shading.Both, "both")];

    [Test]
    public void ASelectedValue_SelectsTheItemWhoseValueItIs_AndAPickWritesItsValue()
    {
        var choices = Choices();
        var control = new SegmentedControl { SelectedValuePath = nameof(Choice.Value), ItemsSource = choices };

        control.SelectedValue = Shading.Wireframe;
        var chosen = control.SelectedItem;
        control.PickFromContainer(Segment(control, 2));

        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.SameAs(choices[1]));
            Assert.That(control.SelectedItem, Is.SameAs(choices[2]));
            Assert.That(control.SelectedValue, Is.EqualTo(Shading.Both));
        });
    }

    [Test]
    public void AValueNamedBeforeTheItems_IsSelectedWhenTheyCome()
    {
        var choices = Choices();
        var control = new SegmentedControl { SelectedValuePath = nameof(Choice.Value) };

        control.SelectedValue = Shading.Both;
        control.ItemsSource = choices;

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedIndex, Is.EqualTo(2));
            Assert.That(control.SelectedValue, Is.EqualTo(Shading.Both));
        });
    }

    [Test]
    public void APathGivenAfterTheValue_StillFindsItsItem()
    {
        var choices = Choices();
        var control = new SegmentedControl();

        control.SelectedValue = Shading.Wireframe;
        control.ItemsSource = choices;
        control.SelectedValuePath = nameof(Choice.Value);

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedItem, Is.SameAs(choices[1]));
            Assert.That(control.SelectedValue, Is.EqualTo(Shading.Wireframe));
        });
    }

    [Test]
    public void ARowRefilledWithANewList_SelectsTheNewItemOfTheSameValue()
    {
        var row = new ChoiceProperty { ValuePath = nameof(Choice.Value), ItemsSource = Choices() };
        var drop = new DropDown();
        row.PrepareEditor(drop, Shading.Both);

        var fresh = Choices();
        row.ItemsSource = fresh;
        row.PrepareEditor(drop, Shading.Both);

        Assert.That(drop.SelectedItem, Is.SameAs(fresh[2]));
    }

    [Test]
    public void WithoutAPath_TheItemIsItsOwnValue()
    {
        var control = Written("Day", "Week");
        var week = control.Items[1];

        control.SelectedIndex = 1;

        Assert.That(control.SelectedValue, Is.SameAs(week));
    }

    [Test]
    public void AnInspectorRow_KeepsTheSegmentsItsEditorBrings_AndTakesTheirValues()
    {
        var choices = Choices();
        var editor = new SegmentedControl();
        foreach (var choice in choices) editor.Items.Add(choice);
        var row = new SegmentedChoiceProperty { ValuePath = nameof(Choice.Value) };

        row.PrepareEditor(editor, Shading.Wireframe);
        var shown = editor.SelectedItem;
        editor.PickFromContainer(Segment(editor, 2));

        Assert.Multiple(() =>
        {
            Assert.That(editor.Items, Has.Count.EqualTo(3), "its own segments stay");
            Assert.That(shown, Is.SameAs(choices[1]));
            Assert.That(row.ReadEditor(editor), Is.EqualTo(Shading.Both));
        });
    }

    [Test]
    public void ToAutomation_ASegmentIsSelected_AsAPressPicksIt()
    {
        var control = Written("Day", "Week", "Month");
        var month = Segment(control, 2);

        ((ISelectionItemProvider)month.GetAutomationPeer().GetPattern(PatternId.SelectionItem)).Select();

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedIndex, Is.EqualTo(2));
            Assert.That(((ISelectionProvider)control.GetAutomationPeer().GetPattern(PatternId.Selection)).CanSelectMultiple, Is.False);
        });
    }
}
