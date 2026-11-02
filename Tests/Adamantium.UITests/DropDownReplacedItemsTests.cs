using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

// A view-model that replaces a drop-down's list under its choice: the header must show a row the list has, never the
// one it had - the choice follows its item, or its value, and is cleared when neither is among the new rows.
[TestFixture]
public class DropDownReplacedItemsTests
{
    private sealed record Choice(int Column, string Name)
    {
        public override string ToString() => Name;
    }

    [Test]
    public void TheChoiceFollowsItsItemToItsNewPlace()
    {
        var drop = new DropDown { ItemsSource = new[] { "a", "b" }, SelectedIndex = 1 };

        drop.ItemsSource = new[] { "b", "c", "a" };

        Assert.That(drop.SelectedIndex, Is.EqualTo(0));
        Assert.That(drop.SelectedItem, Is.EqualTo("b"));
        Assert.That(drop.DisplayContent, Is.EqualTo("b"));
    }

    [Test]
    public void AChoiceWhoseItemIsGone_IsCleared()
    {
        var drop = new DropDown { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };

        drop.ItemsSource = new[] { "c", "d" };

        Assert.That(drop.SelectedIndex, Is.EqualTo(-1), "the header must not keep showing a row the list lost");
        Assert.That(drop.SelectedItem, Is.Null);
        Assert.That(drop.DisplayContent, Is.Not.EqualTo("a"));
    }

    [Test]
    public void WithAValuePath_TheChoiceFollowsItsValueToTheNewRow()
    {
        var drop = new DropDown
        {
            SelectedValuePath = nameof(Choice.Column),
            ItemsSource = new[] { new Choice(0, "Column 1"), new Choice(1, "Column 2") },
            SelectedIndex = 1
        };

        var renamed = new[] { new Choice(0, "Name"), new Choice(1, "Sum") };
        drop.ItemsSource = renamed;

        Assert.That(drop.SelectedIndex, Is.EqualTo(1));
        Assert.That(drop.SelectedItem, Is.SameAs(renamed[1]));
        Assert.That(drop.SelectedValue, Is.EqualTo(1));
        Assert.That(drop.DisplayContent, Is.SameAs(renamed[1]), "the header reads the renamed row");
    }

    [Test]
    public void FollowingTheItems_RunsNoCommand()
    {
        var command = new SwitchableCommand { CanRun = true };
        var drop = new DropDown
        {
            SelectedValuePath = nameof(Choice.Column),
            ItemsSource = new[] { new Choice(0, "Column 1"), new Choice(1, "Column 2") },
            SelectedIndex = 0,
            Command = command
        };

        drop.ItemsSource = new[] { new Choice(0, "Name"), new Choice(1, "Sum") };

        Assert.That(command.Runs, Is.Zero, "nobody picked anything - the list was only replaced");
    }

    // An index named before the rows, the rows then added one by one, as markup adds them: nothing in between clears it.
    [Test]
    public void RowsAddedOneByOne_NeverClearAChoiceWaitingForThem()
    {
        var drop = new DropDown { SelectedIndex = 2 };
        var seen = new System.Collections.Generic.List<int>();
        drop.SelectionChanged += (_, _) => seen.Add(drop.SelectedIndex);

        drop.Items.Add(new DropDownItem { Content = "a" });
        drop.Items.Add(new DropDownItem { Content = "b" });
        var third = new DropDownItem { Content = "c" };
        drop.Items.Add(third);

        Assert.That(drop.SelectedItem, Is.SameAs(third));
        Assert.That(seen, Is.EqualTo(new[] { 2 }), "chosen once, never cleared on the way");
    }

    // An emptied list has no choice left - index, item and header alike - however it is filled again.
    [Test]
    public void AnEmptiedList_ClearsTheChoice()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b" };
        var drop = new DropDown { ItemsSource = items, SelectedIndex = 1, Placeholder = "none" };

        items.Clear();
        items.Add("c");
        items.Add("d");

        Assert.That((drop.SelectedItem, drop.SelectedIndex, drop.DisplayContent), Is.EqualTo(((object)null, -1, "none")));
    }

    // A choice named before its list waits for it, and is chosen when its row comes.
    [Test]
    public void AChoiceNamedBeforeItsList_IsChosenWhenItsRowComes()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>();
        var drop = new DropDown { ItemsSource = items, SelectedItem = "b" };

        items.Add("a");
        items.Add("b");

        Assert.That((drop.SelectedItem, drop.SelectedIndex, drop.DisplayContent), Is.EqualTo(("b", 1, "b")));
    }

    // A value named while the list has no row of it is chosen once a list with one arrives.
    [Test]
    public void AValueNamedBeforeItsRow_IsChosenWhenItArrives()
    {
        var drop = new DropDown { ItemsSource = new[] { 1, 2 } };
        drop.SelectedValue = 5;

        drop.ItemsSource = new[] { 4, 5 };

        Assert.That((drop.SelectedIndex, drop.SelectedItem), Is.EqualTo((1, (object)5)));
    }

    // Of rows alike, the chosen one stays chosen when another row comes.
    [Test]
    public void OfRowsAlike_TheChosenOneStays()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "a" };
        var drop = new DropDown { ItemsSource = items, SelectedIndex = 1 };

        items.Add("b");

        Assert.That(drop.SelectedIndex, Is.EqualTo(1));
    }
}
