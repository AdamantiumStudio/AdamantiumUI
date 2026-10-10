using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What is under a collapsed element is not laid out: a change inside it waits until the element is shown,
/// and is laid out then. It used to be measured and arranged on its own, at the size it asked for, so a hidden
/// control believed it was on screen.</summary>
[TestFixture]
public class CollapsedAncestorLayoutTests
{
    [Test]
    public void AChangeUnderAnElementCollapsedFromTheStart_IsNotLaidOut_UntilItIsShown()
    {
        var probe = new Probe { Width = 30, Height = 10 };
        var hidden = new Border { Visibility = Visibility.Collapsed, Child = new Border { Child = probe } };
        var window = Show(hidden);

        probe.Height = 20;
        WindowExtension.UpdateTree(window);

        Assert.That(probe.Laid, Is.Zero, "nothing laid out while hidden");

        hidden.Visibility = Visibility.Visible;
        WindowExtension.UpdateTree(window);

        Assert.That((probe.Laid, probe.RenderSize), Is.EqualTo((1, new Size(30, 20))));
    }

    [Test]
    public void AChangeUnderAnElementCollapsedAfterItWasShown_IsNotLaidOut_UntilItIsShownAgain()
    {
        var probe = new Probe { Width = 30, Height = 10 };
        var hidden = new Border { Child = new Border { Child = probe } };
        var window = Show(hidden);
        hidden.Visibility = Visibility.Collapsed;
        WindowExtension.UpdateTree(window);
        probe.Laid = 0;

        probe.Height = 20;
        WindowExtension.UpdateTree(window);

        Assert.That(probe.Laid, Is.Zero, "nothing laid out while hidden");

        hidden.Visibility = Visibility.Visible;
        WindowExtension.UpdateTree(window);

        Assert.That((probe.Laid, probe.RenderSize), Is.EqualTo((1, new Size(30, 20))));
    }

    [Test]
    public void AChangeInTheFrameItsAncestorCollapses_IsNotLaidOut_AndTheLayoutSettles()
    {
        var probe = new Probe { Width = 30, Height = 10 };
        var hidden = new Border { Child = new Border { Child = probe } };
        var window = Show(hidden);
        probe.Laid = 0;

        probe.Height = 20;
        hidden.Visibility = Visibility.Collapsed;
        WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Laid, Is.Zero, "nothing laid out while hidden");
            Assert.That(LayoutManager.For(window).IsSettled, Is.True);
        });

        hidden.Visibility = Visibility.Visible;
        WindowExtension.UpdateTree(window);

        Assert.That((probe.Laid, probe.RenderSize), Is.EqualTo((1, new Size(30, 20))));
    }

    [Test]
    public void AChangeUnderAFixedSizeParent_OfACollapsedElement_IsLaidOut_OnlyWhenItIsShown()
    {
        var probe = new Probe { Width = 30, Height = 10 };
        var hidden = new Border { Child = new Border { Width = 100, Height = 50, Child = probe } };
        var window = Show(hidden);
        hidden.Visibility = Visibility.Collapsed;
        WindowExtension.UpdateTree(window);
        probe.Laid = 0;

        probe.Height = 20;
        WindowExtension.UpdateTree(window);

        Assert.That(probe.Laid, Is.Zero, "nothing laid out while hidden");

        hidden.Visibility = Visibility.Visible;
        WindowExtension.UpdateTree(window);

        Assert.That((probe.Laid, probe.RenderSize), Is.EqualTo((1, new Size(30, 20))));
    }

    [Test]
    public void AnArrangeOnlyChange_UnderACollapsedElement_IsArranged_OnlyWhenItIsShown()
    {
        var probe = new Probe { Width = 30, Height = 10, HorizontalAlignment = HorizontalAlignment.Left };
        var hidden = new Border { Width = 100, Child = probe };
        var window = Show(hidden);
        hidden.Visibility = Visibility.Collapsed;
        WindowExtension.UpdateTree(window);
        probe.Laid = 0;

        probe.HorizontalAlignment = HorizontalAlignment.Right;
        WindowExtension.UpdateTree(window);

        Assert.That(probe.Laid, Is.Zero, "nothing laid out while hidden");

        hidden.Visibility = Visibility.Visible;
        WindowExtension.UpdateTree(window);

        Assert.That((probe.Laid, probe.Bounds.X), Is.EqualTo((1, 70d)));
    }

    private static Window Show(Border content)
    {
        var root = new StackPanel();
        root.Children.Add(content);
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = root };
        WindowExtension.UpdateTree(window);
        return window;
    }

    private sealed class Probe : Border
    {
        public int Laid;

        protected override Size ArrangeOverride(Size finalSize)
        {
            Laid++;
            return base.ArrangeOverride(finalSize);
        }
    }
}
