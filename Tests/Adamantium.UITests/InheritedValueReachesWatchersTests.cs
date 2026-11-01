using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// An element that moves under a new parent changes what its whole subtree inherits, and whoever watches one of those
/// values below it must hear so - even when nothing at the moved element itself is watching. A docking pane's tab label,
/// bound to its presenter's FontSize, kept the size of the moment its panel was out of the tree.
/// </summary>
[TestFixture]
public class InheritedValueReachesWatchersTests
{
    [Test]
    public void AWatcherBelowAnUnwatchedElement_HearsTheValueItInheritsAfterAMove()
    {
        var root = new Border { FontSize = 20 };
        var panel = new Border();   // nothing watches its FontSize
        var tab = new Border();
        panel.Child = tab;

        var heard = new List<double>();
        tab.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add(tab.FontSize);
        };

        root.Child = panel;

        Assert.Multiple(() =>
        {
            Assert.That(tab.FontSize, Is.EqualTo(20));
            Assert.That(heard, Does.Contain(20.0), "the tab was never told");
        });
    }

    /// <summary>What is inherited is what an ancestor STATES. An ancestor's mere default must not cover the element's own
    /// theme value - a docking tab moved into a new strip took the strip's transparent foreground.</summary>
    [Test]
    public void AnAncestorsDefault_DoesNotCoverTheElementsThemeValue()
    {
        var first = new Border();
        var second = new Border();
        var tab = new Border { Child = new Border() };   // a subtree below it, as a tab has
        tab.SetValue(UIComponent.FontSizeProperty, 13.0, Adamantium.UI.Core.ValuePriority.TypeDefault);
        tab.PropertyChanged += (_, _) => { };              // watched, as a tab is

        first.Child = tab;
        first.Child = null;
        second.Child = tab;

        Assert.That(tab.FontSize, Is.EqualTo(13));
    }

    /// <summary>A value an element inherited goes when the tree above it is cut off from the ancestor that stated it, even
    /// with nothing at the element watching; back under that ancestor, the watcher below hears it return.</summary>
    [Test]
    public void AnElementPassedOver_LetsGoOfTheValue_WhenTheTreeAboveIsCutOffFromIt()
    {
        var model = new object();
        var holder = new Border { DataContext = model };
        var shownIn = new Border { InheritanceParent = holder };   // a template's presenter
        var content = new Border();                                 // nothing watches its DataContext
        var watcher = new Border();
        content.Child = watcher;
        content.InheritanceParent = shownIn;

        Assert.That(watcher.DataContext, Is.SameAs(model));   // read, as a binding reads it
        var heard = new List<object>();
        watcher.DataContextChanged += (_, e) => heard.Add(e.NewValue);

        shownIn.InheritanceParent = null;   // the old template is cut off
        content.InheritanceParent = holder; // and the content goes back to what holds it

        Assert.Multiple(() =>
        {
            Assert.That(content.DataContext, Is.SameAs(model));
            Assert.That(heard, Is.EqualTo(new object[] { null, model }), "the watcher below heard it go and come back");
        });
    }

    /// <summary>A watcher that reads another element while a change goes down the tree - a binding reading a sibling -
    /// takes nothing from that element and from those under it: each still hears the change when it reaches them.</summary>
    [TestCase(true, TestName = "AReadDuringThePush_KeepsTheChangeForThoseBelow_WhenTheValueIsCleared")]
    [TestCase(false, TestName = "AReadDuringThePush_KeepsTheChangeForThoseBelow_WhenTheValueIsReplaced")]
    public void AReadDuringThePush_KeepsTheChangeForThoseBelow(bool cleared)
    {
        var model = new object();
        var replacement = new object();
        var holder = new Border { DataContext = model };
        var reader = new Border { InheritanceParent = holder };
        var sibling = new Border { InheritanceParent = holder };
        var below = new Border { InheritanceParent = sibling };
        Assert.That(below.DataContext, Is.SameAs(model));
        Assert.That(sibling.DataContext, Is.SameAs(model));
        Assert.That(reader.DataContext, Is.SameAs(model));

        var heardBySibling = new List<object>();
        var heardBelow = new List<object>();
        reader.DataContextChanged += (_, _) => _ = sibling.DataContext;
        sibling.DataContextChanged += (_, e) => heardBySibling.Add(e.NewValue);
        below.DataContextChanged += (_, e) => heardBelow.Add(e.NewValue);

        if (cleared) holder.ClearValue(Adamantium.UI.Core.FundamentalUIComponent.DataContextProperty);
        else holder.DataContext = replacement;

        var expected = new[] { cleared ? null : replacement };
        Assert.Multiple(() =>
        {
            Assert.That(heardBySibling, Is.EqualTo(expected), "the sibling heard it");
            Assert.That(heardBelow, Is.EqualTo(expected), "the element under the sibling heard it");
        });
    }

    /// <summary>An element showing its own theme value, under ancestors that state none, reads that value until one is
    /// stated: the change it hears starts there, and an ancestor stating the same value is no change to it.</summary>
    [TestCase(13.0, new string[0], TestName = "AnElementsOwnThemeValue_IsNoChange_WhenAnAncestorStatesTheSame")]
    [TestCase(20.0, new[] { "13->20" }, TestName = "AnElementsOwnThemeValue_IsWhatItsChangeStartsFrom")]
    public void AnElementsOwnThemeValue_IsWhatItReadBeforeAnAncestorStatesOne(double stated, string[] expected)
    {
        var parent = new Border();
        var child = new Border();
        child.SetValue(UIComponent.FontSizeProperty, 13.0, Adamantium.UI.Core.ValuePriority.TypeDefault);
        parent.Child = child;
        Assert.That(child.FontSize, Is.EqualTo(13.0));
        var heard = new List<string>();
        child.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add($"{e.OldValue}->{e.NewValue}");
        };

        parent.FontSize = stated;

        Assert.That(heard, Is.EqualTo(expected));
    }

    [Test]
    public void UnderAnElementsOwnThemeValue_TheChangeStartsFromWhatWasReadThere()
    {
        var parent = new Border();
        var child = new Border();
        var below = new Border();
        child.SetValue(UIComponent.FontSizeProperty, 13.0, Adamantium.UI.Core.ValuePriority.TypeDefault);
        child.Child = below;
        parent.Child = child;
        var before = below.FontSize;
        var heard = new List<string>();
        below.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.FontSizeProperty) heard.Add($"{e.OldValue}->{e.NewValue}");
        };

        parent.FontSize = 20.0;

        Assert.That(heard, Is.EqualTo(new[] { $"{before}->20" }));
    }

    /// <summary>An element with nothing under it, moved under a parent stating two values, hears both even when its
    /// watcher, told of one, reads the other first.</summary>
    [Test]
    public void AnElementMoved_HearsEveryValueItInherits_WhenItsWatcherReadsAnotherOnTheWay()
    {
        var model = new object();
        var parent = new Border { DataContext = model, FontSize = 20 };
        var leaf = new Border();
        Assert.That(leaf.FontSize, Is.Not.EqualTo(20.0));
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property != Adamantium.UI.Core.FundamentalUIComponent.DataContextProperty &&
                e.Property != UIComponent.FontSizeProperty) return;

            heard.Add(e.Property.Name);
            _ = leaf.FontSize;
            _ = leaf.DataContext;
        };

        parent.Child = leaf;

        Assert.That(heard, Is.EquivalentTo(new[] { "DataContext", "FontSize" }));
    }

    [Test]
    public void AReadDuringThePush_KeepsTheChangeForThoseBelow_WhenTheTreeAboveIsCut()
    {
        var model = new object();
        var holder = new Border { DataContext = model };
        var middle = new Border { InheritanceParent = holder };
        var reader = new Border { InheritanceParent = middle };
        var sibling = new Border { InheritanceParent = middle };
        var below = new Border { InheritanceParent = sibling };
        Assert.That(below.DataContext, Is.SameAs(model));
        Assert.That(sibling.DataContext, Is.SameAs(model));
        Assert.That(reader.DataContext, Is.SameAs(model));

        var heardBelow = new List<object>();
        reader.DataContextChanged += (_, _) => _ = sibling.DataContext;
        sibling.DataContextChanged += (_, _) => { };
        below.DataContextChanged += (_, e) => heardBelow.Add(e.NewValue);

        middle.InheritanceParent = null;

        Assert.That(heardBelow, Is.EqualTo(new object[] { null }));
    }

    [Test]
    public void ABindingToAnInheritedValue_FollowsItAcrossMoves()
    {
        var small = new Border { FontSize = 13 };
        var large = new Border { FontSize = 20 };
        var panel = new Border();
        var tab = new Border();
        panel.Child = tab;

        var label = new TextBlock();
        label.SetBinding(UIComponent.FontSizeProperty, new Binding(nameof(UIComponent.FontSize)) { Source = tab });

        foreach (var (host, size) in new[] { (large, 20.0), (small, 13.0), (large, 20.0) })
        {
            if (panel.LogicalParent is Border previous) previous.Child = null;
            host.Child = panel;
            BindingUpdateQueue.Flush();

            Assert.That(label.FontSize, Is.EqualTo(size), $"under the {size} host");
        }
    }
}
