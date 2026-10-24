using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using Adamantium.UI.Platforms;
using NUnit.Framework;

namespace Adamantium.UITests;

// A popup opened before its window first comes up - a backstage the application starts on - takes the keyboard while
// the window is not active yet; the window's activation must leave it there, not put it on the window's first stop.
[TestFixture]
public class ActivatedWindowFocusTests
{
    [TearDown]
    public void TearDown() => FocusManager.Focus(null);

    [Test]
    public void ActivationLeavesTheFocusInAPopupOfTheWindow()
    {
        var (window, outside, inside) = WindowWithPopup();
        FocusManager.Focus(inside);

        ActivatedWindowFocus.Restore(window);

        Assert.That(FocusManager.Focused, Is.SameAs(inside));
        Assert.That(outside.IsFocused, Is.False);
    }

    [Test]
    public void AWindowSeenForTheFirstTimeStartsAtItsFirstStop()
    {
        var (window, outside, _) = WindowWithPopup();

        ActivatedWindowFocus.Restore(window);

        Assert.That(FocusManager.Focused, Is.SameAs(outside));
    }

    private static (Window Window, Button Outside, Button Inside) WindowWithPopup()
    {
        var outside = new Button { Content = "Outside" };
        var inside = new Button { Content = "Inside" };
        var popup = new Popup { Child = new Border { Child = inside } };
        var panel = new StackPanel();
        panel.Children.Add(outside);
        panel.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        WindowExtension.UpdateTree(window);
        return (window, outside, inside);
    }
}
