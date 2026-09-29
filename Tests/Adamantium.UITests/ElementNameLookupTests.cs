using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary><c>{Binding …, ElementName=X}</c> finds X wherever the markup put it: further down the page, inside content a
/// scroll viewer has not laid out yet - names belong to the markup, not to whatever happens to be realized.</summary>
[TestFixture]
public class ElementNameLookupTests
{
    [Test]
    public void ANameFurtherDownInsideAScrollViewer_IsFound()
    {
        var target = new Border();
        target.SetBinding("Height", new Binding("Width") { ElementName = "Later" });

        var stack = new StackPanel();
        stack.Children.Add(target);
        stack.Children.Add(new ScrollViewer { Content = new Border { Name = "Later", Width = 42 } });

        var window = new Window { Width = 400, Height = 300, Content = stack };
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();
        }

        Assert.That(target.Height, Is.EqualTo(42));
    }

    // A page built INTO a live window: the binding misses while already in the tree, and the element it names arrives a
    // moment later in the same build - declared in the page's name scope, as generated code does, but not laid out yet.
    // One more try before anything is written down.
    [Test]
    public void ANameAddedToALiveWindowAfterTheBinding_IsFoundWithoutAnAlarm()
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var page = new StackPanel();
            var window = new Window { Width = 400, Height = 300, Content = page };
            WindowExtension.UpdateTree(window);

            var target = new Border();
            page.Children.Add(target);
            target.SetBinding("Height", new Binding("Width") { ElementName = "Later" });
            var later = new Border { Name = "Later", Width = 42 };
            NameScope.Register(page, "Later", later);
            page.Children.Add(new ScrollViewer { Content = later });

            WindowExtension.UpdateTree(window);
            BindingUpdateQueue.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(target.Height, Is.EqualTo(42));
                Assert.That(messages, Is.Empty);
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    // ...and a name that is simply not there is written down once, not retried every frame after.
    [Test]
    public void ANameThatNeverArrives_IsReportedOnce()
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var target = new Border();
            var window = new Window { Width = 400, Height = 300, Content = target };
            WindowExtension.UpdateTree(window);

            target.SetBinding("Height", new Binding("Width") { ElementName = "Nowhere" });
            for (var i = 0; i < 3; i++)
            {
                WindowExtension.UpdateTree(window);
                BindingUpdateQueue.Flush();
            }

            Assert.That(messages, Has.Exactly(1).Contains("'Nowhere'"));
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }
}
