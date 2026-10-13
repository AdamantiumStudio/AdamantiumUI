using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// A theme brush lists every element painting with it, so a recolor reaches them all. A discarded element has to leave
// that list - an element that was never in a tree has no leaving of the tree to do it for it, and the brush, which
// lives as long as the theme, kept it and whatever it hangs on.
[TestFixture]
public class BrushOwnerReleaseTests
{
    [Test]
    public void ADiscardedElement_IsNoLongerOwnedByItsBrush()
    {
        var shared = new SolidColorBrush(Colors.Red);
        var element = new Border { Background = shared };
        Assume.That(shared.IsOwnedBy(element), "painting with the brush makes the element its owner");

        DiscardedVisuals.Publish(element);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(shared.IsOwnedBy(element), Is.False);
    }

    // ...and one that never let go - dropped without a discard, or holding the brush by a path that never gives it back -
    // is still not kept by it: the brush outlives every page that paints with it, so it may only know them weakly.
    [Test]
    public void AnElementThatNeverLetGo_IsNotKeptByItsBrush()
    {
        var shared = new SolidColorBrush(Colors.Red);
        var gone = Painting(shared);

        for (var i = 0; i < 3 && gone.IsAlive; i++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False);
        System.GC.KeepAlive(shared);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static System.WeakReference Painting(SolidColorBrush shared)
    {
        var element = new Border { Background = shared };
        Assume.That(shared.IsOwnedBy(element), "painting with the brush makes the element its owner");
        RenderDirty.Clear();

        return new System.WeakReference(element);
    }

    // A run is not in the visual tree, so nothing that walks it reaches the run, and a run painting with a theme brush
    // would keep its block and the page. Only elements own a brush - a run never does, nested in a span or not.
    [Test]
    public void ARunPaintingWithABrush_NeverOwnsIt()
    {
        var shared = new SolidColorBrush(Colors.Red);
        var run = new Adamantium.UI.Controls.Text.Run { Text = "a", Foreground = shared };
        var nested = new Adamantium.UI.Controls.Text.Run { Text = "b", Foreground = shared };
        var bold = new Adamantium.UI.Controls.Text.Bold();
        bold.Inlines.Add(nested);
        var block = new Adamantium.UI.Controls.Text.TextBlock();
        block.Inlines.Add(run);
        block.Inlines.Add(bold);

        Assert.Multiple(() =>
        {
            Assert.That(shared.IsOwnedBy(run), Is.False, "the run");
            Assert.That(shared.IsOwnedBy(nested), Is.False, "the run inside a span");
        });
    }

    // An element coming back into the tree re-takes what it paints with. An inherited brush that changed while it was out
    // is resolved by that very walk, and the resolution took the link as well: the brush held the element twice, a
    // discard gave up once, and the theme brush kept the element for good.
    [Test]
    public void AnInheritedBrushThatChangedWhileTheElementWasOut_LetsGoOfItOnDiscard()
    {
        var shared = new SolidColorBrush(Colors.Red);
        var other = new SolidColorBrush(Colors.Blue);
        var child = new Border();
        var parent = new Border { Foreground = shared, Child = child };
        var window = new Adamantium.UI.Controls.Window { Width = 200, Height = 100, Content = parent };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Assume.That(child.Foreground, Is.SameAs(shared), "the child paints with what its parent passes down");

        parent.Child = null;
        Assume.That(child.RenderAttachmentsReleased, "leaving the tree gives the links up");
        parent.Foreground = other;
        parent.Child = child;
        Assume.That(child.RenderAttachmentsReleased, Is.False, "coming back takes them again");

        DiscardedVisuals.Publish(child);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(other.IsOwnedBy(child), Is.False, "the brush it paints with now");
            Assert.That(shared.IsOwnedBy(child), Is.False, "the brush it painted with before");
        });
    }
}
