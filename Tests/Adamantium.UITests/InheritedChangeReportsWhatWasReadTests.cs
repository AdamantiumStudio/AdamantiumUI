using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// An element told of an inherited change hears it from what it read to what it reads now - whatever an element above it
/// that nothing watches had or had not read, and whether or not that element itself reads differently afterwards.
/// </summary>
[TestFixture]
public class InheritedChangeReportsWhatWasReadTests
{
    private static (Border middle, List<string> heard) UnderAMiddleNothingWatches(Brush stated)
    {
        var grand = new Border();
        var middle = new Border();
        var leaf = new TextBlock();
        middle.Child = leaf;
        grand.Child = middle;
        grand.Foreground = stated;
        Assert.That(leaf.Foreground, Is.SameAs(stated));
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.ForegroundProperty) heard.Add($"{Name(e.OldValue)}->{Name(e.NewValue)}");
        };
        return (middle, heard);
    }

    private static string Name(object brush) =>
        ReferenceEquals(brush, Brushes.Red) ? "Red"
        : ReferenceEquals(brush, Brushes.Blue) ? "Blue"
        : ReferenceEquals(brush, Brushes.White) ? "White"
        : ReferenceEquals(brush, Brushes.Green) ? "Green"
        : brush?.ToString() ?? "null";

    [TestCase("White", TestName = "AValueStatedAbove_ReachesAnElementWhoseDefaultItIs")]
    [TestCase("Green", TestName = "AValueStatedAbove_ReachesAnElementFromWhatItRead")]
    public void AValueStatedAboveByAnElementNothingWatches_ReachesTheElementBelow(string stated)
    {
        var (middle, heard) = UnderAMiddleNothingWatches(Brushes.Red);

        middle.Foreground = stated == "White" ? Brushes.White : Brushes.Green;

        Assert.That(heard, Is.EqualTo(new[] { $"Red->{stated}" }));
    }

    [Test]
    public void AValueStatedAbove_StartsFromWhatWasRead_WhenTheElementAboveHadReadAnOlderOne()
    {
        var grand = new Border();
        var middle = new Border();
        var leaf = new TextBlock();
        middle.Child = leaf;
        grand.Child = middle;
        grand.Foreground = Brushes.Red;
        _ = middle.Foreground;
        grand.Foreground = Brushes.Blue;
        Assert.That(leaf.Foreground, Is.SameAs(Brushes.Blue));
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.ForegroundProperty) heard.Add($"{Name(e.OldValue)}->{Name(e.NewValue)}");
        };

        middle.Foreground = Brushes.Green;

        Assert.That(heard, Is.EqualTo(new[] { "Blue->Green" }));
    }

    [Test]
    public void AValueClearedAbove_ReachesTheElementBelow_AsTheValueStatedFurtherUp()
    {
        var (middle, heard) = UnderAMiddleNothingWatches(Brushes.Red);
        middle.Foreground = Brushes.Green;
        heard.Clear();

        middle.ClearValue(UIComponent.ForegroundProperty);

        Assert.That(heard, Is.EqualTo(new[] { "Green->Red" }));
    }

    [Test]
    public void ADataContextStatedAbove_ReachesTheElementBelow_FromTheContextItHad()
    {
        var grand = new Border();
        var middle = new Border();
        var leaf = new Border();
        var heard = new List<string>();
        leaf.DataContextChanged += (_, e) => heard.Add($"{e.OldValue ?? "null"}->{e.NewValue ?? "null"}");
        middle.Child = leaf;
        grand.Child = middle;
        grand.DataContext = "A";
        Assert.That(leaf.DataContext, Is.EqualTo("A"));
        heard.Clear();

        middle.DataContext = "B";

        Assert.That(heard, Is.EqualTo(new[] { "A->B" }));
    }

    [Test]
    public void AFontSizeStatedAbove_StartsFromWhatWasRead_WhenTheElementAboveNeverReadIt()
    {
        var grand = new Border();
        var middle = new Border();
        var leaf = new TextBlock();
        middle.Child = leaf;
        grand.Child = middle;
        grand.FontSize = 20;
        Assert.That(leaf.FontSize, Is.EqualTo(20.0));
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add($"{e.OldValue}->{e.NewValue}");
        };

        middle.FontSize = 12;

        Assert.That(heard, Is.EqualTo(new[] { "20->12" }));
    }

    [Test]
    public void AReadInTheChangedElementsOwnCallback_StillLetsTheChangeReachTheElementBelow()
    {
        var below = new InheritingProbe();
        var changed = new InheritingProbe { Child = below };
        changed.Level = 1;
        Assert.That(below.Level, Is.EqualTo(1));
        new Border().FontSize = 30;
        var heard = new List<string>();
        changed.LevelChanged += (_, _) => _ = below.Level;
        below.LevelChanged += (_, e) => heard.Add($"{e.OldValue}->{e.NewValue}");

        changed.Level = 2;

        Assert.That(heard, Is.EqualTo(new[] { "1->2" }));
    }

    [Test]
    public void AnElementsOwnValueCleared_GivesWayToWhatItInherits_AndItHearsTheChangesOfThatAfterwards()
    {
        var below = new InheritingProbe { Level = 5 };
        var above = new InheritingProbe { Child = below, Level = 1 };
        Assert.That(below.Level, Is.EqualTo(5));
        var heard = new List<string>();
        below.LevelChanged += (_, e) => heard.Add($"{e.OldValue}->{e.NewValue}");

        below.ClearValue(InheritingProbe.LevelProperty);
        above.Level = 0;

        Assert.Multiple(() =>
        {
            Assert.That(below.Level, Is.EqualTo(0));
            Assert.That(heard, Is.EqualTo(new[] { "5->1", "1->0" }));
        });
    }

    [Test]
    public void AnElementWatchedOnlyAfterAChangeReachedIt_HearsTheNextFromThatChange()
    {
        var leaf = new Border();
        var root = new Border { Child = leaf };
        root.FontSize = 11;
        Assert.That(leaf.FontSize, Is.EqualTo(11));
        root.FontSize = 12;
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add($"{e.OldValue}->{e.NewValue}");
        };

        root.FontSize = 13;

        Assert.That(heard, Is.EqualTo(new[] { "12->13" }));
    }

    /// <summary>A subtree moved under a parent stating what its root already read: the root's child, which read its own
    /// default, now inherits the stated value and hears so.</summary>
    [TestCase(false, TestName = "AnElementUnderAMovedRoot_HearsTheValueItStartsToInherit")]
    [TestCase(true, TestName = "AnElementUnderAMovedWatchedRoot_HearsTheValueItStartsToInherit")]
    public void AnElementUnderAMovedRoot_HearsTheValueItStartsToInherit(bool watchRoot)
    {
        var parent = new Border { FontSize = 14 };
        var root = new Border();
        var leaf = new TextBlock();
        root.Child = leaf;
        if (watchRoot) root.PropertyChanged += (_, _) => { };
        Assert.That(leaf.FontSize, Is.EqualTo(12.0));
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add($"{e.OldValue}->{e.NewValue}");
        };

        parent.Child = root;

        Assert.That(leaf.FontSize, Is.EqualTo(14.0));
        Assert.That(heard, Is.EqualTo(new[] { "12->14" }));
    }
}
