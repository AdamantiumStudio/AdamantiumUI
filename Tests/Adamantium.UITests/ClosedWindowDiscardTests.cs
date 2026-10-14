using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A closed window is never shown again, so it is discarded with everything in it. Kept alive, it stayed an
/// owner of every theme brush it painted with - a window is its own root, so to a brush it never leaves a tree - and
/// its renderer went on recording frames nobody drew: the sandbox's ribbon window, opened and closed once per run of
/// its scenario, kept hundreds of megabytes each time.</summary>
[TestFixture]
public class ClosedWindowDiscardTests
{
    [Test]
    public void AClosedWindow_IsDiscardedWithItsContent()
    {
        var content = new Border();
        var window = new Window { Width = 200, Height = 100, Content = content };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        window.Close();
        LoopSignal.Drain();   // the discard runs on the loop thread, whichever thread closed the window
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(window.IsDiscarded, Is.True, "the window");
            Assert.That(content.IsDiscarded, Is.True, "its content");
        });
    }

    // The focus ring is the window's adorner, not part of its tree, so the tree's discard does not reach it.
    [Test]
    public void AClosedWindow_DiscardsItsFocusRing()
    {
        var button = new Adamantium.UI.Controls.Buttons.Button();
        var window = new Window { Width = 200, Height = 100, Content = button };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        window.AdornerLayer.SetFocus(button);
        var ring = (Adamantium.UI.Controls.Adorners.Adorner)window.Adorners[0];

        window.Close();
        LoopSignal.Drain();   // the discard runs on the loop thread, whichever thread closed the window
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(ring.IsDiscarded, Is.True);
    }

    // Moved to another element, the ring is a new adorner: the old one is gone for good.
    [Test]
    public void AFocusRingReplaced_IsDiscarded()
    {
        var first = new Adamantium.UI.Controls.Buttons.Button();
        var second = new Adamantium.UI.Controls.Buttons.Button();
        var layer = new Adamantium.UI.Controls.Adorners.AdornerLayer();
        layer.SetFocus(first);
        var ring = (Adamantium.UI.Controls.Adorners.Adorner)layer.Adorners[0];

        layer.SetFocus(second);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(ring.IsDiscarded, Is.True);
    }

    // ...and not kept by the element it ringed, which listed it as an inheritance child for as long as the element lived.
    [Test]
    public void AFocusRingReplaced_IsNotKeptByTheElementItRinged()
    {
        var first = new Adamantium.UI.Controls.Buttons.Button();
        var layer = new Adamantium.UI.Controls.Adorners.AdornerLayer();
        var ring = RingOn(layer, first);

        layer.SetFocus(new Adamantium.UI.Controls.Buttons.Button());
        while (DiscardedVisuals.Drain(64) > 0) { }
        RenderDirty.Clear();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();

        Assert.That(ring.IsAlive, Is.False);
        System.GC.KeepAlive(first);
    }

    // Only a discarded ring leaves: the one the element wears now still inherits from it.
    [Test]
    public void ALiveFocusRing_StillInheritsFromItsElement()
    {
        var button = new Adamantium.UI.Controls.Buttons.Button();
        var other = new Adamantium.UI.Controls.Buttons.Button();
        var layer = new Adamantium.UI.Controls.Adorners.AdornerLayer();
        layer.SetFocus(other);
        layer.SetFocus(button);
        while (DiscardedVisuals.Drain(64) > 0) { }
        var ring = (Adamantium.UI.Controls.Adorners.Adorner)layer.Adorners[0];

        button.DataContext = "the button's";

        Assert.That(ring.DataContext, Is.EqualTo("the button's"));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static System.WeakReference RingOn(Adamantium.UI.Controls.Adorners.AdornerLayer layer,
        Adamantium.UI.Controls.Base.UIComponent element)
    {
        layer.SetFocus(element);
        return new System.WeakReference(layer.Adorners[0]);
    }

    [Test]
    public void AWindowWhoseCloseIsCanceled_IsNotDiscarded()
    {
        var window = new Window { Width = 200, Height = 100, Content = new Border() };
        window.Closing += (_, e) => e.Cancel = true;

        window.Close();
        LoopSignal.Drain();   // the discard runs on the loop thread, whichever thread closed the window
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(window.IsDiscarded, Is.False);
    }
}
