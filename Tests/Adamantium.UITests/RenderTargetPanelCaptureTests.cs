using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class RenderTargetPanelCaptureTests
{
    [TearDown]
    public void ReleaseThePointer()
    {
        Mouse.Pointer.Capture(null);
    }

    [Test]
    public void APress_KeepsThePointerUntilItsRelease()
    {
        var panel = Built();

        Press(panel, MouseButtons.Left);

        Assert.That(Mouse.Pointer.Captured, Is.SameAs(panel));

        Release(panel, MouseButtons.Left);

        Assert.That(Mouse.Pointer.Captured, Is.Null);
    }

    [Test]
    public void ReleasingOneOfTwoButtons_KeepsThePointer()
    {
        var panel = Built();
        Press(panel, MouseButtons.Left);
        Press(panel, MouseButtons.Middle);

        Release(panel, MouseButtons.Left);

        Assert.That(Mouse.Pointer.Captured, Is.SameAs(panel));

        Release(panel, MouseButtons.Middle);

        Assert.That(Mouse.Pointer.Captured, Is.Null);
    }

    [Test]
    public void AButtonHeldWhenThePointerIsTaken_IsForgotten()
    {
        var panel = Built();
        Press(panel, MouseButtons.Left);

        Mouse.Pointer.Capture(null);
        Press(panel, MouseButtons.Right);
        Release(panel, MouseButtons.Right);

        Assert.That(Mouse.Pointer.Captured, Is.Null);
    }

    private static RenderTargetPanel Built()
    {
        var panel = new RenderTargetPanel { Width = 200, Height = 100 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return panel;
    }

    private static void Press(RenderTargetPanel panel, MouseButtons button) =>
        ((IObservableComponent)panel).RaiseEvent(
            new PointerPressedEventArgs(Mouse.Pointer, button, InputModifiers.None, 0) { RoutedEvent = PointerEvents.PointerPressedEvent });

    private static void Release(RenderTargetPanel panel, MouseButtons button) =>
        ((IObservableComponent)panel).RaiseEvent(
            new PointerReleasedEventArgs(Mouse.Pointer, button, InputModifiers.None, 0) { RoutedEvent = PointerEvents.PointerReleasedEvent });
}
