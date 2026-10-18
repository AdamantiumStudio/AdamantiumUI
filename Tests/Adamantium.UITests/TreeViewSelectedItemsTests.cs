using System.Collections.ObjectModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A tree's SelectedItems kept in step with a view-model's list both ways: what is written into it is selected,
/// down in closed branches too, and what the user selects is written into it.</summary>
[TestFixture]
public class TreeViewSelectedItemsTests
{
    private sealed class Node
    {
        public Node(string name, Node parent = null)
        {
            Name = name;
            Parent = parent;
            parent?.Children.Add(this);
        }

        public string Name { get; }
        public Node Parent { get; }
        public ObservableCollection<Node> Children { get; } = [];
        public override string ToString() => Name;
    }

    private static TreeView Tree(params Node[] roots) => new()
    {
        ItemTemplate = new HierarchicalDataTemplate(() => new TemplateResult { RootComponent = new Border() })
        {
            ItemsSource = new Binding(nameof(Node.Children))
        },
        ParentPath = nameof(Node.Parent),
        SelectionMode = TreeViewSelectionMode.Extended,
        ItemsSource = new ObservableCollection<Node>(roots)
    };

    private static TreeRow RowOf(TreeView tree, Node node)
    {
        foreach (var item in tree.Items)
        {
            if (item is TreeRow row && row.Node == node)
            {
                return row;
            }
        }

        return null;
    }

    [Test]
    public void NodesWrittenIntoTheList_AreSelected_InClosedBranchesWhenTheirRowsCome()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var light = new Node("light");
        var tree = Tree(model, light);
        var chosen = new ObservableCollection<object>();
        tree.SelectedItems = chosen;

        chosen.Add(light);
        chosen.Add(wing);
        LoopSignal.Drain();
        tree.SetRowExpanded(RowOf(tree, model), true);

        Assert.Multiple(() =>
        {
            Assert.That(RowOf(tree, light).IsSelected, Is.True);
            Assert.That(RowOf(tree, wing).IsSelected, Is.True, "its row came selected");
            Assert.That(RowOf(tree, model).IsSelected, Is.False);
        });
    }

    [Test]
    public void AddingARow_WritesItIntoTheList_AndKeepsWhatIsInClosedBranches()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var light = new Node("light");
        var tree = Tree(model, light);
        var chosen = new ObservableCollection<object> { wing };
        tree.SelectedItems = chosen;

        tree.AddRowToSelection(RowOf(tree, light));

        Assert.That(chosen, Is.EquivalentTo(new object[] { wing, light }));
    }

    [Test]
    public void ChoosingARowAlone_LeavesOnlyItInTheList()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var light = new Node("light");
        var tree = Tree(model, light);
        var chosen = new ObservableCollection<object> { wing, model };
        tree.SelectedItems = chosen;

        tree.SelectOnlyRow(RowOf(tree, light));

        Assert.That(chosen, Is.EquivalentTo(new object[] { light }));
    }

    [Test]
    public void ANodeOfTheListWrittenAsTheChosenOne_LetsNoneOfTheRestGo()
    {
        var model = new Node("model");
        var light = new Node("light");
        var tree = Tree(model, light);
        tree.SelectedItems = new ObservableCollection<object> { model, light };

        tree.SelectedItem = light;

        Assert.Multiple(() =>
        {
            Assert.That(RowOf(tree, model).IsSelected, Is.True);
            Assert.That(RowOf(tree, light).IsSelected, Is.True);
        });
    }
}
