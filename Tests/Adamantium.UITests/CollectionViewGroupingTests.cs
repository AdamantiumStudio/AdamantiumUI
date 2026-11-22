using System.Collections.ObjectModel;
using System.Linq;
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

    [Test]
    public void AnItemAddedOrTakenFromTheSource_ChangesTheGroups_TellingOnce()
    {
        var source = new ObservableCollection<Part> { Box, Body };
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
