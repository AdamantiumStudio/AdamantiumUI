using System;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A dictionary linked at Global scope outlives what linked it, so a discarded element's link keeps the
/// dictionary - and must not keep the element. Each visit to a page linking one rebuilt the page, and every copy stayed
/// in the dictionary's owners for good.</summary>
[TestFixture]
public class GlobalResourceOwnerTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Link(ResourceProvider global)
    {
        var dictionary = new ResourceDictionary();
        dictionary.Add("Accent", "red");
        var owner = new StackPanel();
        global.AddSource(owner, dictionary);
        global.ForgetOwner(owner);
        return new WeakReference(owner);
    }

    [Test]
    public void AForgottenOwner_IsNotHeld_AndItsGlobalResourcesStay()
    {
        var global = new ResourceProvider();
        var owner = Link(global);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(owner.IsAlive, Is.False, "the provider still holds the element");
            Assert.That(global.FindResource("Accent"), Is.EqualTo("red"), "the global resource went with it");
        });
    }
}
