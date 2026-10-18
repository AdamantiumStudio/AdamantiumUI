using System.Collections.ObjectModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A NODE CHOSEN ELSEWHERE IS FOUND IN THE TREE: picked in a scene, an entity deep inside a model has no row
/// while the model is closed, and with the member a node names its parent by the tree opens the way to it.</summary>
[TestFixture]
public class TreeViewRevealTests
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
        public bool Selected { get; set; }
        public override string ToString() => Name;
    }

    private static TreeView Tree(string parentPath, params Node[] roots) => new()
    {
        ItemTemplate = new HierarchicalDataTemplate(() => new TemplateResult { RootComponent = new Border() })
        {
            ItemsSource = new Binding(nameof(Node.Children))
        },
        ParentPath = parentPath,
        ItemsSource = new ObservableCollection<Node>(roots)
    };

    [Test]
    public void ANodeInsideClosedBranches_IsRevealedAndSelected()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var flap = new Node("flap", wing);
        var camera = new Node("camera");
        var tree = Tree(nameof(Node.Parent), model, camera);

        tree.SelectedItem = flap;

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedItem, Is.SameAs(flap));
            Assert.That(tree.Items, Has.Count.EqualTo(4), "model, wing and flap opened, camera after them");
            Assert.That(tree.Items[2], Is.InstanceOf<TreeRow>().And.Property(nameof(TreeRow.IsSelected)).True);
            Assert.That(((TreeRow)tree.Items[2]).Node, Is.SameAs(flap));
        });
    }

    // Without the member nothing is opened: the tree does not know where a node it cannot see sits.
    [Test]
    public void WithoutAParentPath_AHiddenNodeIsNotSelected()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var tree = Tree(null, model);

        tree.SelectedItem = wing;

        Assert.That(tree.Items, Has.Count.EqualTo(1), "nothing opened");
        Assert.That(((TreeRow)tree.Items[0]).IsSelected, Is.False);
    }

    // An entity picked in a scene is marked selected before the tree hears of it, so its row comes marked: it is still
    // the one row selected, and the row chosen before lets go.
    [Test]
    public void ANodeMarkedSelectedAlready_IsTheOneSelected()
    {
        var model = new Node("model");
        var flap = new Node("flap", model);
        var tree = Tree(nameof(Node.Parent));
        var style = new Adamantium.UI.Core.Resources.Style
        {
            Selector = new Adamantium.UI.Core.Resources.StyleSelector { Types = { typeof(TreeViewItem) } }
        };
        style.Setters.Add(new Adamantium.UI.Core.Resources.Setter("IsSelected",
            new Binding(nameof(Node.Selected)) { Mode = BindingMode.TwoWay }));
        tree.ItemContainerStyle = style;
        tree.ItemsSource = new ObservableCollection<Node> { model };
        tree.SelectedItem = model;

        flap.Selected = true;
        tree.SelectedItem = flap;

        Assert.Multiple(() =>
        {
            Assert.That(((TreeRow)tree.Items[0]).IsSelected, Is.False, "the model's row let go");
            Assert.That(((TreeRow)tree.Items[1]).IsSelected, Is.True);
        });
    }

    // With no member that keeps a node's selection, a row built again - its branch closed and opened - is still the one
    // chosen, and letting the choice go reaches it.
    [Test]
    public void ARowBuiltAgain_IsStillTheChosenOne()
    {
        var model = new Node("model");
        var wing = new Node("wing", model);
        var tree = Tree(nameof(Node.Parent), model);
        tree.SelectedItem = wing;

        tree.SetRowExpanded((TreeRow)tree.Items[0], false);
        tree.SetRowExpanded((TreeRow)tree.Items[0], true);
        var rebuilt = (TreeRow)tree.Items[1];
        var chosenAgain = rebuilt.IsSelected;
        tree.SelectedItem = null;

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Node, Is.SameAs(wing));
            Assert.That(chosenAgain, Is.True);
            Assert.That(rebuilt.IsSelected, Is.False, "let go");
        });
    }

    // A node chosen before it is in the tree - an entity picked as it is added - comes selected when its row does.
    [Test]
    public void ARowAddedForTheChosenNode_ComesSelected()
    {
        var roots = new ObservableCollection<Node> { new("model") };
        var tree = Tree(nameof(Node.Parent));
        tree.ItemsSource = roots;
        var light = new Node("light");
        tree.SelectedItem = light;

        roots.Add(light);

        Assert.That(((TreeRow)tree.Items[1]).IsSelected, Is.True);
    }

    // An entity made and selected at once, while another was selected: the row chosen before lets go at once, not left
    // selected beside the new one.
    [Test]
    public void ANodeChosenBeforeItsRowComes_LetsTheOldRowGo()
    {
        var model = new Node("model");
        var roots = new ObservableCollection<Node> { model };
        var tree = Tree(nameof(Node.Parent));
        tree.ItemsSource = roots;
        tree.SelectedItem = model;
        var light = new Node("light");

        tree.SelectedItem = light;
        var modelLetGo = !((TreeRow)tree.Items[0]).IsSelected;
        roots.Add(light);

        Assert.Multiple(() =>
        {
            Assert.That(modelLetGo, Is.True, "before the new row came");
            Assert.That(((TreeRow)tree.Items[0]).IsSelected, Is.False);
            Assert.That(((TreeRow)tree.Items[1]).IsSelected, Is.True);
            Assert.That(tree.SelectedItem, Is.SameAs(light));
        });
    }

    // Where a node keeps its own selection, the node chosen before its row came is marked, so its row comes selected.
    [Test]
    public void ANodeChosenBeforeItsRowComes_IsMarked_WhereNodesKeepTheirSelection()
    {
        var model = new Node("model");
        var roots = new ObservableCollection<Node> { model };
        var tree = Tree(nameof(Node.Parent));
        var style = new Adamantium.UI.Core.Resources.Style
        {
            Selector = new Adamantium.UI.Core.Resources.StyleSelector { Types = { typeof(TreeViewItem) } }
        };
        style.Setters.Add(new Adamantium.UI.Core.Resources.Setter("IsSelected",
            new Binding(nameof(Node.Selected)) { Mode = BindingMode.TwoWay }));
        tree.ItemContainerStyle = style;
        tree.ItemsSource = roots;
        tree.SelectedItem = model;
        var light = new Node("light");

        tree.SelectedItem = light;
        roots.Add(light);

        Assert.Multiple(() =>
        {
            Assert.That(model.Selected, Is.False);
            Assert.That(light.Selected, Is.True);
            Assert.That(((TreeRow)tree.Items[1]).IsSelected, Is.True);
        });
    }

    // A tree made after the choice - the hierarchy shown once the scene had a selection - opens to it.
    [Test]
    public void ANodeChosenBeforeTheRoots_IsRevealedWhenTheyCome()
    {
        var model = new Node("model");
        var flap = new Node("flap", model);
        var tree = Tree(nameof(Node.Parent));
        tree.SelectedItem = flap;

        tree.ItemsSource = new ObservableCollection<Node> { model };

        Assert.That(tree.SelectedItem, Is.SameAs(flap));
        Assert.That(tree.Items, Has.Count.EqualTo(2));
    }

    // A parent chain that comes round again is not followed for ever.
    [Test]
    public void AParentChainInACircle_EndsTheSearch()
    {
        var a = new Looping("a");
        var b = new Looping("b") { Parent = a };
        a.Parent = b;
        var tree = new TreeView { ParentPath = nameof(Looping.Parent), ItemsSource = new[] { new Looping("root") } };

        tree.SelectedItem = a;

        Assert.That(tree.Items, Has.Count.EqualTo(1));
    }

    private sealed class Looping(string name)
    {
        public string Name { get; } = name;
        public Looping Parent { get; set; }
    }

    private sealed class Moving(string name)
    {
        public string Name { get; } = name;
        public Moving Parent { get; set; }
        public ObservableCollection<Moving> Children { get; } = [];
        public override string ToString() => Name;
    }

    private static TreeView MovingTree(ObservableCollection<Moving> roots) => new()
    {
        ItemTemplate = new HierarchicalDataTemplate(() => new TemplateResult { RootComponent = new Border() })
        {
            ItemsSource = new Binding(nameof(Moving.Children))
        },
        ParentPath = nameof(Moving.Parent),
        ItemsSource = roots
    };

    // A camera hung on a part of a closed model goes with its row: the tree opens the way to where it went.
    [Test]
    public void AChosenNodeMovedIntoAClosedBranch_IsFollowedThere()
    {
        var model = new Moving("model");
        var wing = new Moving("wing") { Parent = model };
        model.Children.Add(wing);
        var camera = new Moving("camera");
        var roots = new ObservableCollection<Moving> { model, camera };
        var tree = MovingTree(roots);
        tree.SelectedItem = camera;

        roots.Remove(camera);
        camera.Parent = wing;
        wing.Children.Add(camera);
        LoopSignal.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedItem, Is.SameAs(camera));
            Assert.That(tree.Items, Has.Count.EqualTo(3), "model and wing opened to it");
            Assert.That(((TreeRow)tree.Items[2]).Node, Is.SameAs(camera));
            Assert.That(((TreeRow)tree.Items[2]).IsSelected, Is.True);
        });
    }

    [Test]
    public void ABranchClosedOverTheChosenNode_StaysClosed()
    {
        var model = new Moving("model");
        var wing = new Moving("wing") { Parent = model };
        model.Children.Add(wing);
        var tree = MovingTree([model]);
        tree.SelectedItem = wing;

        tree.SetRowExpanded((TreeRow)tree.Items[0], false);
        LoopSignal.Drain();

        Assert.That(tree.Items, Has.Count.EqualTo(1));
    }

    // A node not under these roots at all opens nothing.
    [Test]
    public void ANodeFromElsewhere_OpensNothing()
    {
        var model = new Node("model");
        new Node("wing", model);
        var stranger = new Node("stranger", new Node("other"));
        var tree = Tree(nameof(Node.Parent), model);

        tree.SelectedItem = stranger;

        Assert.That(tree.Items, Has.Count.EqualTo(1));
    }
}
