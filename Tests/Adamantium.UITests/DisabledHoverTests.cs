using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An element marked <c>ToolTipService.ShowOnDisabled</c> is still found by the pointer inside a disabled
/// ancestor - so its tooltip can say why the rest cannot be used; nothing else inside a disabled element is.</summary>
[TestFixture]
public class DisabledHoverTests
{
    private static (StackPanel Panel, Image Marked, Image Plain) Laid(bool disabled)
    {
        var marked = new Image { Width = 20, Height = 20 };
        ToolTipService.SetShowOnDisabled(marked, true);
        var plain = new Image { Width = 20, Height = 20 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, IsEnabled = !disabled };
        panel.Children.Add(marked);
        panel.Children.Add(plain);
        panel.Measure(new Size<double>(200, 50), force: true);
        panel.Arrange(new Rect(0, 0, 200, 50));
        return (panel, marked, plain);
    }

    [Test]
    public void InADisabledParent_TheMarkedElement_IsHovered()
    {
        var (panel, marked, _) = Laid(disabled: true);

        Assert.That(panel.HitTest(new Vector2<double>(10, 10), hover: true), Is.SameAs(marked));
    }

    [Test]
    public void InADisabledParent_APressOnTheMarkedElement_ReachesNoneInside()
    {
        var (panel, marked, _) = Laid(disabled: true);

        Assert.That(panel.HitTest(new Vector2<double>(10, 10)), Is.Not.SameAs(marked));
    }

    [Test]
    public void InADisabledParent_AButtonMarked_IsNotClickedByAPress()
    {
        var clicks = 0;
        var button = new Button { Width = 40, Height = 20, ClickMode = ClickMode.Press };
        button.Click += (_, _) => clicks++;
        ToolTipService.SetShowOnDisabled(button, true);
        var panel = new StackPanel { IsEnabled = false };
        panel.Children.Add(button);
        panel.Measure(new Size<double>(200, 50), force: true);
        panel.Arrange(new Rect(0, 0, 200, 50));

        if (panel.HitTest(new Vector2<double>(10, 10)) is { } pressed) PointerInput.Press(pressed);

        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void InADisabledParent_AnUnmarkedElement_IsNot()
    {
        var (panel, _, plain) = Laid(disabled: true);

        Assert.That(panel.HitTest(new Vector2<double>(30, 10), hover: true), Is.Not.SameAs(plain));
    }

    [Test]
    public void InAnEnabledParent_BothAreHitAsAlways()
    {
        var (panel, marked, plain) = Laid(disabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(panel.HitTest(new Vector2<double>(10, 10)), Is.SameAs(marked));
            Assert.That(panel.HitTest(new Vector2<double>(30, 10)), Is.SameAs(plain));
        });
    }
}
