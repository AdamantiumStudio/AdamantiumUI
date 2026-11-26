using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using Adamantium.UI.Platforms.MacOS.Accessibility;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The elements the Mac's accessibility gets, asked the way VoiceOver asks them - the methods of the
/// <c>NSAccessibility</c> protocol, through the Objective-C runtime.</summary>
[TestFixture]
[Platform("MacOsX")]
public unsafe class MacOSAccessibilityTests
{
    private IntPtr _pool;
    private StackPanel _root;
    private Window _window;
    private MacOSAccessibility _accessibility;
    private int _posted;

    [SetUp]
    public void SetUp()
    {
        _pool = ObjCRuntime.PushAutoreleasePool();
        _root = new StackPanel();
        _window = new Window { Width = 400, Height = 300, Content = _root };
        _posted = 0;
        _accessibility = new MacOSAccessibility(() => _window.GetAutomationPeer(), IntPtr.Zero,
            pixels => new CGRect(pixels.X / 2, 1000 - pixels.Bottom / 2, pixels.Width / 2, pixels.Height / 2),
            point => new PixelPoint((int)(point.X * 2), (int)((1000 - point.Y) * 2)),
            work =>
            {
                _posted++;
                work();
            });
    }

    [TearDown]
    public void TearDown()
    {
        _accessibility.Dispose();
        ObjCRuntime.PopAutoreleasePool(_pool);
    }

    [Test]
    public void AnElement_HasItsRoleNameAndValue_ReadOnTheLoop()
    {
        var check = new CheckBox { Content = "Bold", IsChecked = true };
        var element = ElementOf(check);
        var before = _posted;

        // Asked on a thread of its own, as AppKit asks on its main thread: a pooled one may have been taken for the loop's
        // by a test before.
        object[] answers = null;
        var asking = new System.Threading.Thread(() =>
        {
            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                answers =
                [
                    Text(element, "accessibilityRole"), Text(element, "accessibilityLabel"), Number(element, "accessibilityValue"),
                    Flag(element, "isAccessibilityElement"), Flag(element, "isAccessibilityEnabled")
                ];
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        });
        asking.Start();
        asking.Join();

        Assert.Multiple(() =>
        {
            Assert.That(answers[0], Is.EqualTo("AXCheckBox"));
            Assert.That(answers[1], Is.EqualTo("Bold"));
            Assert.That(answers[2], Is.EqualTo(1));
            Assert.That(answers[3], Is.True);
            Assert.That(answers[4], Is.True);
            Assert.That(_posted - before, Is.EqualTo(5), "every question is answered on the loop");
        });
    }

    [Test]
    public void APress_TogglesAndInvokes_AsTheElementDoes()
    {
        var check = new CheckBox { Content = "Bold" };
        var clicks = 0;
        var button = new Button { Content = "Go" };
        button.Click += (_, _) => clicks++;

        Assert.Multiple(() =>
        {
            Assert.That(Flag(ElementOf(check), "accessibilityPerformPress"), Is.True);
            Assert.That(check.IsChecked, Is.True);
            Assert.That(Flag(ElementOf(button), "accessibilityPerformPress"), Is.True);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(Text(ElementOf(button), "accessibilityRole"), Is.EqualTo("AXButton"));
        });
    }

    [Test]
    public void OnlyWhatTheElementDoes_IsOffered()
    {
        var label = ElementOf(new TextBlock { Text = "Plain" });
        var slider = ElementOf(new Slider { Minimum = 0, Maximum = 10, Value = 4, SmallChange = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(Allowed(label, "accessibilityPerformPress"), Is.False);
            Assert.That(Allowed(label, "accessibilityPerformIncrement"), Is.False);
            Assert.That(Allowed(label, "accessibilityPerformShowMenu"), Is.False, "no context menu, none offered");
            Assert.That(Allowed(slider, "accessibilityPerformIncrement"), Is.True);
            Assert.That(Allowed(slider, "setAccessibilityValue:"), Is.True);
            Assert.That(Allowed(label, "accessibilityLabel"), Is.True, "what is not gated is AppKit's to decide");
        });
    }

    [Test]
    public void ARange_IsReadAndStepped()
    {
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 4, SmallChange = 1 };
        var element = ElementOf(slider);

        Assert.Multiple(() =>
        {
            Assert.That(Text(element, "accessibilityRole"), Is.EqualTo("AXSlider"));
            Assert.That(Number(element, "accessibilityValue"), Is.EqualTo(4));
            Assert.That(Number(element, "accessibilityMinValue"), Is.EqualTo(0));
            Assert.That(Number(element, "accessibilityMaxValue"), Is.EqualTo(10));
        });

        Flag(element, "accessibilityPerformIncrement");
        Assert.That(slider.Value, Is.EqualTo(5));
        SetNumber(element, 9);
        Assert.That(slider.Value, Is.EqualTo(9));
    }

    [Test]
    public void APassword_IsSecure_AndShowsNothing()
    {
        var element = ElementOf(new PasswordBox { Password = "hunter2" });

        Assert.Multiple(() =>
        {
            Assert.That(Text(element, "accessibilitySubrole"), Is.EqualTo("AXSecureTextField"));
            Assert.That(NSObject.Send(element, Selector("accessibilityValue")), Is.EqualTo(IntPtr.Zero));
        });
    }

    [Test]
    public void AnExpander_OpensAndShuts()
    {
        var expander = new Expander { Header = "More", IsExpanded = false };
        var element = ElementOf(expander);

        NSObject.Send(element, Selector("setAccessibilityExpanded:"), true);
        Assert.That(expander.IsExpanded, Is.True);
        Assert.That(Flag(element, "isAccessibilityExpanded"), Is.True);
        NSObject.Send(element, Selector("setAccessibilityExpanded:"), false);
        Assert.That(expander.IsExpanded, Is.False);
    }

    [Test]
    public void TheWindowsElements_AreItsChildren_TheSameElementEachTime()
    {
        var first = new Button { Content = "One" };
        var second = new Button { Content = "Two" };
        _root.Children.Add(first);
        _root.Children.Add(second);
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var children = _accessibility.Children();

        // Counted first: an index past the end throws inside the runtime and takes the test run down.
        Assert.That(NSArray.CountOf(children), Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(NSArray.ObjectAt(children, 0), Is.EqualTo(ElementOf(first)));
            Assert.That(NSArray.ObjectAt(children, 1), Is.EqualTo(ElementOf(second)));
            Assert.That(NSObject.Send(ElementOf(first), Selector("accessibilityParent")), Is.EqualTo(IntPtr.Zero),
                "the window's own stand in its view, here none");
        });
    }

    [Test]
    public void AFrame_IsOnAppKitsDesktop()
    {
        var button = new Button { Content = "Go", Width = 40, Height = 20 };
        _root.Children.Add(button);
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var pixels = button.GetAutomationPeer().BoundingRectangle;
        var frame = ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)ObjCRuntime.MessageSend)(ElementOf(button), Selector("accessibilityFrame"));

        // The test's desktop: half a pixel a point, its bottom at 1000 points.
        Assert.Multiple(() =>
        {
            Assert.That(frame.Origin.X, Is.EqualTo(pixels.X / 2).Within(1e-6));
            Assert.That(frame.Origin.Y, Is.EqualTo(1000 - pixels.Bottom / 2).Within(1e-6));
            Assert.That(frame.Size.Width, Is.EqualTo(pixels.Width / 2).Within(1e-6));
            Assert.That(frame.Size.Height, Is.EqualTo(pixels.Height / 2).Within(1e-6));
        });
    }

    [Test]
    public void AnElementLetGo_AnswersNothing()
    {
        var element = ElementOf(new Button { Content = "Go" });
        NSObject.Retain(element);
        try
        {
            _accessibility.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(NSObject.Send(element, Selector("accessibilityLabel")), Is.EqualTo(IntPtr.Zero));
                Assert.That(Flag(element, "accessibilityPerformPress"), Is.False);
            });
        }
        finally
        {
            NSObject.Release(element);
        }
    }

    private IntPtr ElementOf(UIComponent component) => _accessibility.ElementOf(component.GetAutomationPeer());

    private static IntPtr Selector(string name) => ObjCRuntime.RegisterSelector(name);

    private static string Text(IntPtr element, string selector) => NSString.ToManaged(NSObject.Send(element, Selector(selector)));

    private static double Number(IntPtr element, string selector) =>
        NSObject.SendForDouble(NSObject.Send(element, Selector(selector)), Selector("doubleValue"));

    private static bool Flag(IntPtr element, string selector) => NSObject.SendForBoolean(element, Selector(selector));

    private static bool Allowed(IntPtr element, string selector) =>
        NSObject.SendForBoolean(element, Selector("isAccessibilitySelectorAllowed:"), Selector(selector));

    private static void SetNumber(IntPtr element, double value)
    {
        var number = ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr>)ObjCRuntime.MessageSend)(
            ObjCRuntime.GetClass("NSNumber"), Selector("numberWithDouble:"), value);
        NSObject.Send(element, Selector("setAccessibilityValue:"), number);
    }
}
