using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

// The focus manager remembers, per focus scope, the element focused in it last. It lives as long as the application, so
// a scope that is dropped without leaving a tree - and so without letting go of the focus - must not be kept by that.
[TestFixture]
public class FocusScopeReleaseTests
{
    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [Test]
    public void AScopeThatOnceHeldTheFocus_IsNotKeptByTheFocusManager()
    {
        var next = new Button();
        var gone = FocusedThenLeft(next);

        for (var i = 0; i < 3 && gone.IsAlive; i++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False);
        System.GC.KeepAlive(next);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static System.WeakReference FocusedThenLeft(Button next)
    {
        var first = new Button();
        FocusManager.Focus(first);
        Assume.That(FocusManager.Focused, Is.SameAs(first), "the first takes the focus");
        FocusManager.Focus(next);
        Assume.That(FocusManager.Focused, Is.SameAs(next), "...and gives it on");

        return new System.WeakReference(first);
    }
}
