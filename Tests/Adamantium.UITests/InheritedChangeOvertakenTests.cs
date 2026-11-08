using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using NUnit.Framework;

namespace Adamantium.UITests;

// An element told of an inherited DataContext may take one of its own while it is told - its own DataContext bound to
// a member of the arriving one. Its children are then told of its own; the change it was told of must not reach them
// after that, or a binding below resolves against the window's view model. And when what it takes is the very value
// that arrived, the children must still be told of it.
[TestFixture]
public class InheritedChangeOvertakenTests
{
    private sealed class Pane;

    private sealed class Window
    {
        public Pane Shown { get; } = new();
    }

    [Test]
    public void AnElementTakingAnotherDataContext_WhileTold_LeavesItsChildrenOnItsOwn()
    {
        var (root, pane, button) = Tree();
        object told = null;
        button.DataContextChanged += (_, e) => told = e.NewValue;
        pane.DataContextChanged += (_, e) =>
        {
            if (e.NewValue is Window arriving)
            {
                pane.DataContext = arriving.Shown;
            }
        };

        var window = new Window();
        root.DataContext = window;

        Assert.That(told, Is.SameAs(window.Shown), "the button was last told of the window's view model");
    }

    [Test]
    public void AnElementTakingTheDataContextThatArrived_WhileTold_StillTellsItsChildren()
    {
        var (root, pane, button) = Tree();
        object told = null;
        button.DataContextChanged += (_, e) => told = e.NewValue;
        pane.DataContextChanged += (_, e) =>
        {
            if (e.NewValue is Window arriving)
            {
                pane.DataContext = arriving;
            }
        };

        var window = new Window();
        root.DataContext = window;

        Assert.That(told, Is.SameAs(window), "the button was never told");
    }

    private static (Grid Root, Border Pane, Button Button) Tree()
    {
        var root = new Grid();
        var between = new Grid();
        var pane = new Border();
        var button = new Button();
        root.Children.Add(between);
        between.Children.Add(pane);
        pane.Child = button;
        return (root, pane, button);
    }
}
