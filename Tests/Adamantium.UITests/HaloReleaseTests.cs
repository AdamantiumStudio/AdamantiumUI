using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// A glow or a shadow is often one instance shared from a view model, and it outlives every element lit with it. An
// element listens to it to re-record when it changes - and a discarded one went on listening, which kept it and its page.
[TestFixture]
public class HaloReleaseTests
{
    [Test]
    public void ADiscardedElement_IsNotKeptByASharedAuraOrShadow()
    {
        var aura = new Aura();
        var shadow = new Shadow();
        var gone = LitAndDiscarded(aura, shadow);

        for (var i = 0; i < 3 && gone.IsAlive; i++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False);
        System.GC.KeepAlive(aura);
        System.GC.KeepAlive(shadow);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static System.WeakReference LitAndDiscarded(Aura aura, Shadow shadow)
    {
        var element = new Border { Aura = aura, Shadow = shadow };
        DiscardedVisuals.Publish(element);
        while (DiscardedVisuals.Drain(64) > 0) { }
        RenderDirty.Clear();

        return new System.WeakReference(element);
    }
}
