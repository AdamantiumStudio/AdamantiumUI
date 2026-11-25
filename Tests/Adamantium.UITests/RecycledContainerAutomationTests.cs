using System.Collections.Generic;
using Adamantium.Core.Collections;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An item taken out of a list leaves its container in the pool, hidden, for the next item: that container is
/// no part of the list to automation, which would find the taken-out item's elements in it still.</summary>
[TestFixture]
public class RecycledContainerAutomationTests
{
    [Test]
    public void AnItemTakenOut_LeavesNothingOfItInTheListToAutomation()
    {
        var items = new TrackingCollection<string> { "Debris", "Ghosts", "Layer" };
        var list = new ItemsControl
        {
            ItemsSource = items,
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Button { Name = "Remove" } }),
            Template = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };
        var window = new Window { Width = 400, Height = 300, Content = list };
        Settle(window);
        Assert.That(Buttons(list), Is.EqualTo(3));

        items.RemoveAt(2);
        Settle(window);
        var afterRemoval = Buttons(list);
        items.Add("Spirits");
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(afterRemoval, Is.EqualTo(2));
            // The pooled container handed out again is a child once more.
            Assert.That(Buttons(list), Is.EqualTo(3));
        });
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static int Buttons(ItemsControl list)
    {
        var count = 0;
        var stack = new Stack<AutomationPeer>(list.GetAutomationPeer().GetChildren());
        while (stack.Count > 0)
        {
            var peer = stack.Pop();
            count += peer.AutomationId == "Remove" ? 1 : 0;
            foreach (var child in peer.GetChildren())
                stack.Push(child);
        }

        return count;
    }
}
