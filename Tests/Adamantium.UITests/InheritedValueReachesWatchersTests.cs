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
