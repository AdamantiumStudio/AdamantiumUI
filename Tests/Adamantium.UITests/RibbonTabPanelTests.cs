using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The strip's own panel: headers in a row, and before any run of NEIGHBORING tabs sharing a contextual group,
/// that group's label - in the row, so a context never changes the strip's height. It replaces the TabPanel the strip
/// borrowed, so it also has to keep what that one did - above all sizing headers to their content rather than to the
/// slot.</summary>
[TestFixture]
public class RibbonTabPanelTests
{
    private static RibbonTabHeader Header(double width, RibbonContextualGroup group = null)
    {
        var header = new RibbonTabHeader { Width = width, Height = 24, ContextualGroup = group };
        return header;
    }

    private static RibbonTabPanel Strip(params RibbonTabHeader[] headers)
    {
        var panel = new RibbonTabPanel();
        foreach (var header in headers) panel.Children.Add(header);
        return panel;
    }

    private static void LayoutAt(RibbonTabPanel panel, double width, double height = 60)
    {
        ((IMeasurableComponent)panel).Measure(new Size<double>(width, height));
        ((IMeasurableComponent)panel).Arrange(new Rect(0, 0, width, height));
    }

    // Untitled by default: a titled group puts a label before its run, so a test about anything else states no title
    // and the tabs stand where their widths put them.
    private static RibbonContextualGroup Group(string header = null) =>
        new() { Header = header, IsActive = true };

    [Test]
    public void WithNoContextTheStripIsJustItsHeaders()
    {
        var panel = Strip(Header(80), Header(90));
        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(24));
            Assert.That(panel.Children[0].Bounds.Y, Is.Zero);
        });
    }

    // The whole point of the label standing IN the row: switching to a page where a context appears must not make the
    // strip jump.
    [Test]
    public void AContextNeverChangesTheStripsHeight()
    {
        var mesh = Group("MESH TOOLS");
        var panel = Strip(Header(80), Header(90, mesh));
        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(24));
            Assert.That(panel.Children[1].Bounds.Y, Is.Zero, "the contextual tab stands in the same row");
            Assert.That(Labels(panel)[0].Bounds.Height, Is.EqualTo(24), "and so does its label, at the tabs' height");
        });
    }

    // The strip occupies only what it stacks - honest bounds, exactly what TabPanel did. Asserted on the PANEL, because
    // a header with a stated Height would report that height whatever slot it was handed, and prove nothing.
    [Test]
    public void TheStripTakesOnlyTheHeightOfItsHeaders()
    {
        var panel = Strip(Header(80));
        LayoutAt(panel, 400, height: 200);

        Assert.That(panel.Bounds.Height, Is.EqualTo(24), "not the 200 it was offered");
    }

    // Contextual tabs stand AFTER the ordinary ones whatever order they were authored in, with ONE label before the
    // run rather than one per tab.
    [Test]
    public void ContextualTabsGoToTheEndAfterOneLabel()
    {
        var mesh = Group("MESH TOOLS");
        var a = Header(50, mesh);
        var ordinary = Header(70);
        var b = Header(60, mesh);
        var panel = Strip(a, ordinary, b);

        LayoutAt(panel, 600);

        var label = Labels(panel)[0];
        var title = label.DesiredSize.Width;

        Assert.Multiple(() =>
        {
            Assert.That(Labels(panel), Has.Count.EqualTo(1), "one run, one label");
            Assert.That(title, Is.GreaterThan(0), "a title takes room");
            Assert.That(ordinary.Bounds.X, Is.Zero, "the ordinary tab comes first");
            Assert.That(label.Bounds, Is.EqualTo(new Rect(70, 0, title, 24)), "then the label");
            Assert.That(a.Bounds.X, Is.EqualTo(70 + title), "then the group's tabs, brought together");
            Assert.That(b.Bounds.X, Is.EqualTo(120 + title));
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(180 + title), "the label costs its width, nothing more");
        });
    }

    // Off, the context is carried by its tabs' color alone: no label, and the row is exactly its tabs.
    [Test]
    public void AGroupThatHidesItsHeaderHasNoLabel()
    {
        var mesh = Group("MESH TOOLS");
        mesh.ShowContextualHeader = false;

        var panel = Strip(Header(80), Header(90, mesh));
        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(Labels(panel), Is.Empty);
            Assert.That(panel.DesiredSize, Is.EqualTo(new Size<double>(170, 24)));
            Assert.That(panel.Children[1].Bounds.X, Is.EqualTo(80));
        });
    }

    // Switching the header off and on again moves the tabs sideways and never up or down.
    [Test]
    public void TurningTheHeaderOffAndOnMovesTheTabsOnlySideways()
    {
        var mesh = Group("MESH TOOLS");
        var contextual = Header(90, mesh);
        var panel = Strip(Header(80), contextual);
        LayoutAt(panel, 400);

        mesh.ShowContextualHeader = false;
        ((IMeasurableComponent)panel).InvalidateMeasure();
        LayoutAt(panel, 400);

        var offLabels = Labels(panel).Count;
        var offX = contextual.Bounds.X;
        var offHeight = panel.DesiredSize.Height;

        mesh.ShowContextualHeader = true;
        ((IMeasurableComponent)panel).InvalidateMeasure();
        LayoutAt(panel, 400);

        var label = Labels(panel)[0];

        Assert.Multiple(() =>
        {
            Assert.That(offLabels, Is.Zero, "off: no label");
            Assert.That(offX, Is.EqualTo(80));
            Assert.That(offHeight, Is.EqualTo(24));
            Assert.That(label.Bounds, Is.EqualTo(new Rect(80, 0, label.DesiredSize.Width, 24)), "on: the label is back");
            Assert.That(contextual.Bounds.X, Is.EqualTo(80 + label.DesiredSize.Width));
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(24));
        });
    }

    // Two groups, oldest activation first - tabs that appeared LAST stand furthest right and do not shift what someone
    // was already aiming at.
    [Test]
    public void GroupsStandInTheOrderTheyBecameActive()
    {
        var first = Group();
        var second = Group();
        first.ActivatedAt = 1;
        second.ActivatedAt = 2;

        var late = Header(60, second);
        var early = Header(50, first);
        var panel = Strip(late, early);

        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(early.Bounds.X, Is.Zero);
            Assert.That(late.Bounds.X, Is.EqualTo(50));
        });
    }

    // A hidden tab (its group is off) is not in the strip at all - and takes its label with it.
    [Test]
    public void AnInactiveGroupsTabsAreNotInTheRow()
    {
        var mesh = Group("MESH TOOLS");
        var hidden = Header(50, mesh);
        hidden.Visibility = Visibility.Collapsed;
        var panel = Strip(Header(80), hidden);

        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(80));
            Assert.That(Labels(panel), Is.Empty);
        });
    }

    // The arrows walk the strip AS LAID OUT, which is not the authored order once a context is in it; the label names
    // tabs and is never a destination.
    [Test]
    public void TheArrowsWalkTheRowAsItStands()
    {
        var mesh = Group("MESH TOOLS");
        var contextual = Header(50, mesh);
        var ordinary = Header(70);
        var panel = Strip(contextual, ordinary);
        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Navigate(ordinary, FocusNavigationDirection.Right), Is.SameAs(contextual),
                "over the label");
            Assert.That(panel.Navigate(contextual, FocusNavigationDirection.Left), Is.SameAs(ordinary));
            Assert.That(panel.Navigate(contextual, FocusNavigationDirection.Right), Is.Null, "the end of the row");
            Assert.That(panel.Navigate(ordinary, FocusNavigationDirection.Down), Is.Null, "a ROW answers nothing across");
        });
    }

    // Two contexts are two labels, each before its own run.
    [Test]
    public void TwoContextsAreTwoLabels()
    {
        var mesh = Group("MESH");
        var light = Group("LIGHT");
        mesh.ActivatedAt = 1;
        light.ActivatedAt = 2;

        var meshTab = Header(50, mesh);
        var lightTab = Header(60, light);
        var panel = Strip(meshTab, lightTab);
        LayoutAt(panel, 600);

        var labels = Labels(panel);

        Assert.Multiple(() =>
        {
            Assert.That(labels, Has.Count.EqualTo(2));
            Assert.That(labels[0].Bounds.X, Is.Zero);
            Assert.That(meshTab.Bounds.X, Is.EqualTo(labels[0].Bounds.Right));
            Assert.That(labels[1].Bounds.X, Is.EqualTo(meshTab.Bounds.Right));
            Assert.That(lightTab.Bounds.X, Is.EqualTo(labels[1].Bounds.Right));
        });
    }

    // The author's order is kept WITHIN a group. List.Sort is not stable, so ordering by activation alone let a group's
    // own tabs shuffle among themselves.
    [Test]
    public void AGroupsTabsKeepTheOrderTheyWereWrittenIn()
    {
        var mesh = Group();
        mesh.ActivatedAt = 1;

        var geometry = Header(50, mesh);
        var uv = Header(60, mesh);
        var materials = Header(70, mesh);
        var panel = Strip(geometry, uv, materials);

        LayoutAt(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Bounds.X, Is.Zero);
            Assert.That(uv.Bounds.X, Is.EqualTo(50));
            Assert.That(materials.Bounds.X, Is.EqualTo(110));
        });
    }

    // A tab keeps its own width next to a label: nothing is widened to fit a title any more.
    [Test]
    public void ANarrowTabKeepsItsWidthBesideALongTitle()
    {
        var group = Group("A VERY LONG CONTEXT TITLE");
        var only = Header(40, group);
        var panel = Strip(only);
        LayoutAt(panel, 600);

        Assert.That(panel.DesiredSize.Width, Is.EqualTo(Labels(panel)[0].DesiredSize.Width + 40));
    }

    private static System.Collections.Generic.List<RibbonContextualLabel> Labels(RibbonTabPanel panel)
    {
        var found = new System.Collections.Generic.List<RibbonContextualLabel>();
        foreach (var child in panel.VisualChildren)
        {
            if (child is RibbonContextualLabel label) found.Add(label);
        }

        return found;
    }
}
