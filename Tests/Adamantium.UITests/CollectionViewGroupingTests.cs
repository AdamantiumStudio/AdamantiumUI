using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.UI.Core.Collections;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A collection view's groups: the shown items by a property, each group in the order its first item comes -
/// after the filter and the ordering - nested by more levels, and following every change of what is shown.</summary>
[TestFixture]
public class CollectionViewGroupingTests
{
    public sealed record Part(string Name, string Kind, string Size);

    private static readonly Part Box = new("Box", "Collider", "Small");
    private static readonly Part Body = new("Body", "Physics", "Large");
    private static readonly Part Sphere = new("Sphere", "Collider", "Large");
    private static readonly Part Hinge = new("Hinge", "Joint", "Small");

    private static object[] Names(CollectionView view) => view.Groups.Select(group => group.Name).ToArray();

    [Test]
    public void TheShownItems_AreGroupedByTheProperty_InTheOrderOfTheirFirstItem()
    {
        var view = new CollectionView(new[] { Box, Body, Sphere, Hinge });

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));

        Assert.Multiple(() =>
        {
            Assert.That(view.IsGrouped, Is.True);
            Assert.That(Names(view), Is.EqualTo(new object[] { "Collider", "Physics", "Joint" }));
            Assert.That(view.Groups[0].Items, Is.EqualTo(new object[] { Box, Sphere }));
            Assert.That(view.Groups[0].ItemCount, Is.EqualTo(2));
            Assert.That(view.Groups[0].IsBottomLevel, Is.True);
        });
    }

    [Test]
    public void TheFilterAndTheOrdering_ComeFirst_AGroupLeftEmptyGoes()
    {
        var view = new CollectionView(new[] { Box, Body, Sphere, Hinge });
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));

        view.SortDescriptions.Add(new SortDescription(nameof(Part.Name), SortDirection.Descending));
        var sorted = Names(view);
        view.Filter = item => ((Part)item).Name != "Hinge";

        Assert.Multiple(() =>
        {
            Assert.That(sorted, Is.EqualTo(new object[] { "Collider", "Joint", "Physics" }), "Sphere, Hinge, Box, Body");
            Assert.That(Names(view), Is.EqualTo(new object[] { "Collider", "Physics" }));
            Assert.That(view.Groups[0].Items, Is.EqualTo(new object[] { Sphere, Box }));
        });
    }

    // A list shows a group's header and then its items, so the view itself goes group by group - the ordering kept inside
    // each group, the groups where their first item comes.
    [Test]
    public void Grouped_TheItemsComeGroupByGroup_InTheirOrderWithin()
    {
        var view = new CollectionView(new[] { Box, Body, Sphere, Hinge });
        view.SortDescriptions.Add(new SortDescription(nameof(Part.Name), SortDirection.Ascending));
        var sorted = view.Cast<object>().ToArray();

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));

        Assert.Multiple(() =>
        {
            Assert.That(sorted, Is.EqualTo(new object[] { Body, Box, Hinge, Sphere }), "by name, across the kinds");
            Assert.That(view.Cast<object>(), Is.EqualTo(new object[] { Body, Box, Sphere, Hinge }), "Physics, Collider, Joint");
            Assert.That(view[2], Is.SameAs(Sphere));
            Assert.That(Names(view), Is.EqualTo(new object[] { "Physics", "Collider", "Joint" }));
        });
    }

    [Test]
    public void MoreLevels_GoGroupByGroupWithinTheirGroup()
    {
        var view = new CollectionView(new[] { Box, Body, Sphere, Hinge, new Part("Ray", "Collider", "Small") });

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Size)));

        Assert.That(view.Cast<Part>().Select(part => part.Name), Is.EqualTo(new[] { "Box", "Ray", "Sphere", "Body", "Hinge" }));
    }

    // An item joins its group where the ordering puts it, told as one Add: what a list shows of the rest stays.
    [Test]
    public void AnItemJoiningItsGroup_IsOneAddInTheGroup()
    {
        var source = new TrackingCollection<Part> { Box, Body };
        var view = new CollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var said = new List<string>();
        view.CollectionChanged += (_, e) => said.Add($"{e.Action} {e.NewStartingIndex} {e.OldStartingIndex}");

        source.Add(Sphere);
        source.Add(Hinge);
        source.Remove(Hinge);

        Assert.Multiple(() =>
        {
            Assert.That(said, Is.EqualTo(new[] { "Add 1 -1", "Add 3 -1", "Remove -1 3" }));
            Assert.That(view.Cast<object>(), Is.EqualTo(new object[] { Box, Sphere, Body }));
            Assert.That(Names(view), Is.EqualTo(new object[] { "Collider", "Physics" }));
        });
    }

    // A group's first item taken, its next can come after another group's first: the groups change order.
    [Test]
    public void TakingAGroupsFirstItem_CanMoveTheGroupBack()
    {
        var source = new TrackingCollection<Part> { Box, Body, Sphere };
        var view = new CollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var actions = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, e) => actions.Add(e.Action);

        source.Remove(Box);

        Assert.Multiple(() =>
        {
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Reset }));
            Assert.That(Names(view), Is.EqualTo(new object[] { "Physics", "Collider" }), "Body now comes first");
        });
    }

    // A group goes where its first item comes; an item that brings its group before another changes the groups' order,
    // and the view is made again.
    [Test]
    public void AnItemBringingItsGroupForward_IsAReset()
    {
        var source = new TrackingCollection<Part> { Box, Body };
        var view = new CollectionView(source);
        view.SortDescriptions.Add(new SortDescription(nameof(Part.Name), SortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var actions = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, e) => actions.Add(e.Action);
        var before = Names(view);

        source.Add(new Part("Axle", "Collider", "Small"));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new object[] { "Physics", "Collider" }), "Body before Box");
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Reset }));
            Assert.That(Names(view), Is.EqualTo(new object[] { "Collider", "Physics" }), "Axle before Body");
            Assert.That(view.Cast<Part>().Select(part => part.Name), Is.EqualTo(new[] { "Axle", "Box", "Body" }));
        });
    }

    // An item replaced in the source by one of another group, where taking the old one changes the groups' order: the
    // new item is shown once.
    [Test]
    public void AReplacedItem_IsShownOnce_EvenWhenTheGroupsChangeOrder()
    {
        var source = new TrackingCollection<Part> { Box, Body, Sphere };
        var view = new CollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));

        source[0] = Hinge;

        Assert.Multiple(() =>
        {
            Assert.That(view.Count, Is.EqualTo(3));
            Assert.That(view.Cast<Part>().Select(part => part.Name), Is.EqualTo(new[] { "Hinge", "Body", "Sphere" }));
        });
    }

    public sealed class Changing(string kind) : System.ComponentModel.INotifyPropertyChanged
    {
        private string _kind = kind;

        public string Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Kind)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // With no live grouping, an item whose group changes stays where it is shown until the view is made again: the next
    // change of the source does not split its old group into two of one name.
    [Test]
    public void AnItemWhoseGroupChanged_StaysInItsGroup_UntilTheViewIsMadeAgain()
    {
        var first = new Changing("A");
        var source = new TrackingCollection<Changing> { first, new Changing("A"), new Changing("B") };
        var view = new CollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Changing.Kind)));

        first.Kind = "B";
        source.Add(new Changing("C"));

        Assert.That(Names(view), Is.EqualTo(new object[] { "A", "B", "C" }));
    }

    [Test]
    public void AnItemAddedOrTakenFromTheSource_ChangesTheGroups_TellingOnce()
    {
        var source = new TrackingCollection<Part> { Box, Body };
        var view = new CollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var told = 0;
        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionView.Groups))
                told++;
        };
        object[] seenByHandler = null;
        view.CollectionChanged += (_, _) => seenByHandler = Names(view);

        source.Add(Hinge);
        var added = Names(view);
        var toldForAdd = told;
        source.Remove(Body);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(new object[] { "Collider", "Physics", "Joint" }));
            Assert.That(toldForAdd, Is.EqualTo(1));
            Assert.That(Names(view), Is.EqualTo(new object[] { "Collider", "Joint" }));
            Assert.That(seenByHandler, Is.EqualTo(new object[] { "Collider", "Joint" }), "a handler sees the groups after");
        });
    }

    [Test]
    public void MoreLevels_NestGroupsInGroups()
    {
        var view = new CollectionView(new[] { Box, Body, Sphere, Hinge });

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Size)));
        var colliders = view.Groups[0];
        var bySize = colliders.Items.Cast<CollectionViewGroup>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(colliders.IsBottomLevel, Is.False);
            Assert.That(colliders.ItemCount, Is.EqualTo(2));
            Assert.That(bySize.Select(group => group.Name), Is.EqualTo(new object[] { "Small", "Large" }));
            Assert.That(bySize[1].Items, Is.EqualTo(new object[] { Sphere }));
        });
    }

    [Test]
    public void WithoutDescriptions_ThereAreNoGroups()
    {
        var view = new CollectionView(new[] { Box, Body });
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));

        view.GroupDescriptions.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(view.IsGrouped, Is.False);
            Assert.That(view.Groups, Is.Empty);
            Assert.That(view.Cast<object>(), Is.EqualTo(new object[] { Box, Body }));
        });
    }
}
