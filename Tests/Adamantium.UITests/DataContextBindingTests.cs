using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A binding ON DataContext - a panel showing the item picked in a list - reads the context the element stands
/// in, the parent's. Read from the element itself, it read back its own answer on the next refresh and broke: the panel
/// stayed on the first item.</summary>
[TestFixture]
public class DataContextBindingTests
{
    private sealed class Item
    {
        public string Name { get; init; }
    }

    private sealed class Shell : INotifyPropertyChanged
    {
        private Item _picked;

        public Item Picked
        {
            get => _picked;
            set
            {
                _picked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Picked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private static (Border Panel, StackPanel Parent, Window Window) Bound(Shell shell)
    {
        var panel = new Border();
        panel.SetBinding("DataContext", new Binding(nameof(Shell.Picked)) { IsImmediate = true });
        var parent = new StackPanel { DataContext = shell };
        parent.Children.Add(panel);
        var window = new Window { Width = 200, Height = 100, Content = parent };
        Settle(window);
        return (panel, parent, window);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void ABindingOnDataContext_FollowsThePick()
    {
        var first = new Item { Name = "first" };
        var second = new Item { Name = "second" };
        var shell = new Shell { Picked = first };
        var (panel, _, window) = Bound(shell);
        Assert.That(panel.DataContext, Is.SameAs(first), "precondition: the first pick is shown");

        shell.Picked = second;
        Settle(window);

        Assert.That(panel.DataContext, Is.SameAs(second));
    }

    [Test]
    public void ABindingOnDataContext_FollowsTheParentsContext()
    {
        var shell = new Shell { Picked = new Item { Name = "first" } };
        var (panel, parent, window) = Bound(shell);
        var other = new Shell { Picked = new Item { Name = "other" } };

        parent.DataContext = other;
        Settle(window);

        Assert.That(panel.DataContext, Is.SameAs(other.Picked));
    }
}
