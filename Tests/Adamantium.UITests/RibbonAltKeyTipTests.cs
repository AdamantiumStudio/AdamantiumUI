using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Alt let go alone shows the key tips; Alt that a pointer press made half of a gesture - Alt and the right
/// button orbit a 3D view - shows none, however long it was held and repeated.</summary>
[TestFixture]
public class RibbonAltKeyTipTests
{
    [Test]
    public void AltAlone_ShowsTheKeyTips()
    {
        var (window, ribbon) = RibbonInAWindow();

        Alt(window, Keyboard.PreviewKeyDownEvent);
        Alt(window, Keyboard.PreviewKeyUpEvent);

        Assert.That(ribbon.ShowsKeyTips, Is.True);
    }

    [Test]
    public void APressUnderAlt_ShowsNoKeyTips_ThoughAltRepeatsAfter()
    {
        var (window, ribbon) = RibbonInAWindow();

        Alt(window, Keyboard.PreviewKeyDownEvent);
        window.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Right,
            InputModifiers.LeftAlt | InputModifiers.RightMouseButton, default)
        {
            RoutedEvent = PointerEvents.PreviewPointerPressedEvent
        });
        // Held through the drag, Alt repeats; the platform flags the first repeat as a fresh stroke.
        Alt(window, Keyboard.PreviewKeyDownEvent);
        Alt(window, Keyboard.PreviewKeyUpEvent);

        Assert.That(ribbon.ShowsKeyTips, Is.False);
    }

    // A ribbon put away - collapsed itself, or in a collapsed part of the window, as when an application shows its tool
    // bars instead - leaves Alt to the window: no key tips over a strip nobody sees, no letters taken from the text.
    [TestCase(false)]
    [TestCase(true)]
    public void AHiddenRibbon_LeavesAltAlone(bool hiddenByItsParent)
    {
        var ribbon = new Ribbon();
        ribbon.Items.Add(new RibbonTab { Header = "Home" });
        var parent = new Adamantium.UI.Controls.Decorators.Border { Child = ribbon };
        var window = new Window { Width = 400, Height = 200, Content = parent };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        if (hiddenByItsParent)
        {
            parent.Visibility = Visibility.Collapsed;
        }
        else
        {
            ribbon.Visibility = Visibility.Collapsed;
        }

        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var down = Alt(window, Keyboard.PreviewKeyDownEvent);
        Alt(window, Keyboard.PreviewKeyUpEvent);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.ShowsKeyTips, Is.False);
            Assert.That(down.Handled, Is.False, "Alt goes on to the window");
        });
    }

    // Put away while its key tips are up, the ribbon ends them: the next letter goes on to the window, not to a badge.
    [Test]
    public void ARibbonPutAwayWithItsKeyTipsUp_EndsThem_AndLeavesTheLetters()
    {
        var (window, ribbon) = RibbonInAWindow();
        Alt(window, Keyboard.PreviewKeyDownEvent);
        Alt(window, Keyboard.PreviewKeyUpEvent);
        var up = ribbon.ShowsKeyTips;

        ribbon.Visibility = Visibility.Collapsed;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var letter = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.H, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        window.RaiseEvent(letter);

        Assert.Multiple(() =>
        {
            Assert.That(up, Is.True);
            Assert.That(ribbon.ShowsKeyTips, Is.False);
            Assert.That(letter.Handled, Is.False, "the letter goes on to the window");
        });
    }

    private static KeyEventArgs Alt(Window window, RoutedEvent routedEvent)
    {
        var e = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.LeftAlt, InputModifiers.LeftAlt, 0)
        {
            RoutedEvent = routedEvent
        };
        window.RaiseEvent(e);
        return e;
    }

    private static (Window Window, Ribbon Ribbon) RibbonInAWindow()
    {
        var ribbon = new Ribbon();
        ribbon.Items.Add(new RibbonTab { Header = "Home" });
        var window = new Window { Width = 400, Height = 200, Content = ribbon };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        return (window, ribbon);
    }
}
