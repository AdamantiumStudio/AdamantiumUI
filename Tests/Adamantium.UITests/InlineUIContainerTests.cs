using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A control set into a TextBlock's line: it is the block's visual child and its container's logical one, takes
/// the room it asks for on the baseline, raises a line it is taller than, follows its own size and leaves with its
/// container; automation finds it under the block.</summary>
[TestFixture]
public class InlineUIContainerTests
{
    private static Window Host(TextBlock text)
    {
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        Update(window);
        return window;
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static (TextBlock Text, Border Child, InlineUIContainer Container) Block(double width, double height)
    {
        var child = new Border { Width = width, Height = height };
        var container = new InlineUIContainer { Child = child };
        var text = new TextBlock { FontSize = 20 };
        text.Inlines.Add(new Run { Text = "ab" });
        text.Inlines.Add(container);
        text.Inlines.Add(new Run { Text = "cd" });
        return (text, child, container);
    }

    private static float LeftOf(TextBlock text, int index) =>
        text.Layout.GetTextData().Single(glyph => glyph.PositionInString == index).Rect.X;

    [Test]
    public void TheChild_IsTheBlocksVisualChild_AndItsContainersLogicalOne()
    {
        var (text, child, container) = Block(40, 10);
        text.DataContext = "context";
        Host(text);

        Assert.That(child.VisualParent, Is.SameAs(text));
        Assert.That(child.LogicalParent, Is.SameAs(container));
        Assert.That(child.DataContext, Is.EqualTo("context"), "it inherits through its container");
        Assert.That(text.Layout.Text, Is.EqualTo("ab" + (char)0xFFFC + "cd"));
    }

    [Test]
    public void AChildWithoutText_StandsOnTheBaseline_InTheRoomItTakes()
    {
        var (text, child, _) = Block(40, 10);
        Host(text);
        var line = text.Layout.GetLine(0);

        Assert.That(child.Bounds.Width, Is.EqualTo(40).Within(1e-3));
        Assert.That(child.Bounds.Y + child.Bounds.Height, Is.EqualTo(line.Baseline).Within(1e-3));
        Assert.That(LeftOf(text, 3) - child.Bounds.X, Is.GreaterThanOrEqualTo(40 - 1e-3), "the text goes on after it");
    }

    [Test]
    public void AChildWithText_SetsItsTextOnTheLinesBaseline()
    {
        var label = new TextBlock { Text = "OK", FontSize = 14 };
        var button = new Border { Padding = new Thickness(8, 6, 8, 9), Child = label };
        var text = new TextBlock { FontSize = 20 };
        text.Inlines.Add(new Run { Text = "press " });
        text.Inlines.Add(new InlineUIContainer { Child = button });
        text.Inlines.Add(new Run { Text = " now" });
        Host(text);

        var labelBaseline = button.Bounds.Y + label.Bounds.Y + label.Layout.GetLine(0).Baseline;
        Assert.That(labelBaseline, Is.EqualTo(text.Layout.GetLine(0).Baseline).Within(1));
    }

    [Test]
    public void CenteredByItsContainer_ItStandsInTheMiddleOfTheLine()
    {
        var (text, child, container) = Block(40, 10);
        container.BaselineAlignment = BaselineAlignment.Center;
        Host(text);
        var line = text.Layout.GetLine(0);

        Assert.That(child.Bounds.Y + child.Bounds.Height / 2, Is.EqualTo(line.Top + line.Height / 2).Within(1));
    }

    [Test]
    public void ATallChild_RaisesTheLine()
    {
        var (text, child, _) = Block(40, 60);
        Host(text);

        Assert.That(text.DesiredSize.Height, Is.GreaterThan(60));
        Assert.That(child.Bounds.Y, Is.EqualTo(text.Layout.GetLine(0).Top).Within(1));
    }

    [Test]
    public void TheChildGrowing_LaysTheTextOutAgain()
    {
        var (text, child, _) = Block(40, 10);
        var window = Host(text);
        var before = LeftOf(text, 3);

        child.Width = 80;
        Update(window);

        Assert.That(child.Bounds.Width, Is.EqualTo(80).Within(1e-3));
        Assert.That(LeftOf(text, 3) - before, Is.EqualTo(40).Within(1e-3));
    }

    [Test]
    public void AlignedAnew_TheChildFollowsTheText()
    {
        var (text, child, _) = Block(40, 10);
        text.Width = 400;
        var window = Host(text);
        var left = child.Bounds.X;

        text.HorizontalTextAlignment = HorizontalTextAlignment.Right;
        Update(window);

        Assert.That(child.Bounds.X, Is.GreaterThan(left + 200));
    }

    [Test]
    public void AChildInASpan_IsHostedToo()
    {
        var child = new Border { Width = 30, Height = 10 };
        var span = new Bold();
        span.Inlines.Add(new Run { Text = "x" });
        span.Inlines.Add(new InlineUIContainer { Child = child });
        var text = new TextBlock();
        text.Inlines.Add(span);
        Host(text);

        Assert.That(child.VisualParent, Is.SameAs(text));
        Assert.That(child.Bounds.Width, Is.EqualTo(30).Within(1e-3));
    }

    [Test]
    public void RemovedOrEmptied_TheContainerTakesItsChildAway()
    {
        var (text, child, container) = Block(40, 10);
        var window = Host(text);

        container.Child = null;
        Update(window);

        Assert.That(child.VisualParent, Is.Null);
        Assert.That(text.Layout.Text, Is.EqualTo("abcd"));

        container.Child = child;
        Update(window);
        Assert.That(child.VisualParent, Is.SameAs(text));

        text.Inlines.Remove(container);
        Update(window);
        Assert.That(child.VisualParent, Is.Null);
        Assert.That(text.HostedChildren, Is.Empty);
    }

    [Test]
    public void MovedToAnotherContainer_ItIsHostedOnce()
    {
        var (text, child, first) = Block(40, 10);
        var second = new InlineUIContainer();
        text.Inlines.Add(second);
        var window = Host(text);

        second.Child = child;
        Update(window);

        Assert.That(first.Child, Is.Null);
        Assert.That(text.HostedChildren, Is.EqualTo(new[] { child }));
        Assert.That(text.Layout.Text.Count(symbol => symbol == (char)0xFFFC), Is.EqualTo(1));
    }

    [Test]
    public void ABlockOfFixedSize_FollowsItsChildsSize()
    {
        var (text, child, _) = Block(40, 10);
        text.Width = 300;
        text.Height = 60;
        var window = Host(text);
        var before = LeftOf(text, 3);

        child.Width = 80;
        Update(window);

        Assert.That(LeftOf(text, 3) - before, Is.EqualTo(40).Within(1e-3));
    }

    [Test]
    public void TheBlocksFontSize_ReachesTheChild()
    {
        var inner = new TextBlock { Text = "in" };
        var text = new TextBlock { FontSize = 14 };
        text.Inlines.Add(new Run { Text = "out " });
        text.Inlines.Add(new InlineUIContainer { Child = inner });
        var window = Host(text);
        var before = inner.DesiredSize.Width;

        text.FontSize = 28;
        Update(window);

        Assert.That(inner.FontSize, Is.EqualTo(28));
        Assert.That(inner.DesiredSize.Width, Is.GreaterThan(before * 1.5));
    }

    [Test]
    public void FocusOnTheChild_LeavesTheBlocksLinksAlone()
    {
        var button = new Button { Content = "go", Width = 40, Height = 20 };
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "link" });
        var text = new TextBlock();
        text.Inlines.Add(link);
        text.Inlines.Add(new InlineUIContainer { Child = button });
        Host(text);

        button.Focus();

        Assert.That(text.FocusedLink, Is.Null);
    }

    [Test]
    public void Automation_FindsTheChild_TheNameLeavesItOut()
    {
        var button = new Button { Content = "go", Width = 40, Height = 20 };
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "Press " });
        text.Inlines.Add(new InlineUIContainer { Child = button });
        text.Inlines.Add(new Run { Text = " now" });
        Host(text);
        var peer = text.GetAutomationPeer();

        Assert.That(peer.GetChildren(), Does.Contain(button.GetAutomationPeer()));
        Assert.That(peer.Name, Is.EqualTo("Press  now"));
    }
}
