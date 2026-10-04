using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A tree and a ribbon tab make their own container for every item, whatever the template - a TreeViewItem per node, a
/// RibbonGroup per group - and the template draws the item. Only a HierarchicalDataTemplate used to get them: a plain
/// one left bare content, and a tree drew its own internal row object in place of the node.
/// </summary>
[TestFixture]
public class HeaderedContainersTests
{
    private sealed class Node
    {
        public string Name { get; init; }

        public override string ToString() => Name;
    }

    private sealed class Picking : DataTemplateSelector
    {
        public DataTemplate Template { get; init; }

        public override DataTemplate SelectTemplate(object item, AdamantiumComponent container) => Template;
    }

    private static DataTemplate Label() => new(() => new TemplateResult { RootComponent = new TextBlock() });

    [Test]
    public void APlainTemplate_StillMakesTreeNodes_ThatShowTheNodeItself()
    {
        var node = new Node { Name = "a" };
        var template = Label();
        var tree = new TreeView { ItemTemplate = template, ItemsSource = new List<Node> { node } };

        var row = tree.ItemContainerGenerator.Realize(0) as TreeViewItem;

        Assert.That(row, Is.Not.Null, "a tree node, not bare content");
        Assert.Multiple(() =>
        {
            Assert.That(row.Header, Is.SameAs(node), "the node, not the tree's own row behind it");
            Assert.That(row.HeaderTemplate, Is.SameAs(template));
            Assert.That(row.DataContext, Is.SameAs(node));
        });
    }

    [Test]
    public void ATreeWithNoTemplate_ShowsItsNodes()
    {
        var node = new Node { Name = "a" };
        var tree = new TreeView { ItemsSource = new List<Node> { node } };

        Assert.That((tree.ItemContainerGenerator.Realize(0) as TreeViewItem)?.Header, Is.SameAs(node));
    }

    [Test]
    public void ATemplateSelector_DrawsTreeNodes()
    {
        var template = Label();
        var tree = new TreeView
        {
            ItemTemplateSelector = new Picking { Template = template },
            ItemsSource = new List<Node> { new() { Name = "a" } }
        };

        Assert.That((tree.ItemContainerGenerator.Realize(0) as TreeViewItem)?.HeaderTemplate, Is.SameAs(template));
    }

    [Test]
    public void ATemplateSelector_DrawsMenuRows()
    {
        var template = Label();
        var menu = new ContextMenu
        {
            ItemTemplateSelector = new Picking { Template = template },
            ItemsSource = new List<Node> { new() { Name = "Open" } }
        };

        Assert.That((menu.ItemContainerGenerator.Realize(0) as Adamantium.UI.Controls.Primitives.MenuItem)?.HeaderTemplate,
            Is.SameAs(template));
    }

    [Test]
    public void ANodesOwnChildren_AreTreeNodesToo()
    {
        var parent = new TreeViewItem { ItemTemplate = Label() };

        Assert.That(parent.GetContainerForItem(new Node { Name = "a1" }), Is.InstanceOf<TreeViewItem>());
    }

    [Test]
    public void APlainTemplate_StillMakesRibbonGroups()
    {
        var group = new Node { Name = "Clipboard" };
        var template = Label();
        var tab = new RibbonTab { ItemTemplate = template, ItemsSource = new List<Node> { group } };

        var container = tab.ItemContainerGenerator.Realize(0) as RibbonGroup;

        Assert.That(container, Is.Not.Null, "a ribbon group, not bare content");
        Assert.Multiple(() =>
        {
            Assert.That(container.Header, Is.SameAs(group));
            Assert.That(container.HeaderTemplate, Is.SameAs(template));
        });
    }
}
