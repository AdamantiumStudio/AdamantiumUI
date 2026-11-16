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

    private static void Alt(Window window, RoutedEvent routedEvent) =>
        window.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.LeftAlt, InputModifiers.LeftAlt, 0)
        {
            RoutedEvent = routedEvent
        });

    private static (Window Window, Ribbon Ribbon) RibbonInAWindow()
    {
        var ribbon = new Ribbon();
        ribbon.Items.Add(new RibbonTab { Header = "Home" });
        var window = new Window { Width = 400, Height = 200, Content = ribbon };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        return (window, ribbon);
    }
}
