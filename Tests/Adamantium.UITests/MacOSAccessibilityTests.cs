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
        _told.Clear();
        _accessibility = Bridge(() => false, TimeProvider.System);
    }

    private MacOSAccessibility Bridge(Func<bool> assistiveTechnologyOn, TimeProvider clock) =>
        new(() => _window.GetAutomationPeer(), IntPtr.Zero,
            pixels => new CGRect(pixels.X / 2, 1000 - pixels.Bottom / 2, pixels.Width / 2, pixels.Height / 2),
            point => new PixelPoint((int)(point.X * 2), (int)((1000 - point.Y) * 2)),
            work =>
            {
                _posted++;
                work();
            },
            (element, notification) =>
            {
                // Handed held, as AppKit's thread would get it; let go of once told.
                _told.Add((element, notification));
                NSObject.Release(element);
            },
            assistiveTechnologyOn: assistiveTechnologyOn, clock: clock);

    // A steady clock the test moves: its ticks are TimeSpan's.
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private readonly System.Collections.Generic.List<(IntPtr Element, string Notification)> _told = [];

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
            Assert.That(_posted - before, Is.EqualTo(6), "every question is answered on the loop, what the element can be once");
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
            Assert.That(Allowed(label, "isAccessibilityExpanded"), Is.False, "not one that opens: told neither open nor shut");
            Assert.That(Allowed(label, "isAccessibilitySelected"), Is.False);
            Assert.That(Allowed(slider, "accessibilityMinValue"), Is.True);
            Assert.That(Allowed(label, "accessibilityMinValue"), Is.False);
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

    [Test]
    public void AChangedValue_IsTold_OfAnElementAskedAbout()
    {
        var asked = new CheckBox { Content = "Bold" };
        var never = new CheckBox { Content = "Italic" };
        _root.Children.Add(asked);
        _root.Children.Add(never);
        Settle();
        var element = ElementOf(asked);

        asked.IsChecked = true;
        never.IsChecked = true;

        Assert.That(_told, Has.Member((element, "AXValueChanged")));
        Assert.That(_told.FindAll(told => told.Notification == "AXValueChanged"), Has.Count.EqualTo(1),
            "an element nobody asked about is not made to be told of");
    }

    [Test]
    public void TheKeyboardMoving_IsTold_OfTheElementThatTookIt()
    {
        var first = new Button { Content = "One" };
        var second = new Button { Content = "Two" };
        _root.Children.Add(first);
        _root.Children.Add(second);
        Settle();
        ElementOf(first);

        Adamantium.UI.Core.Automation.AutomationEvents.Raise(second, Adamantium.UI.Core.Automation.AutomationEvent.FocusChanged);

        Assert.That(_told, Has.Member((ElementOf(second), "AXFocusedUIElementChanged")));
    }

    [Test]
    public void AnotherWindowsChange_IsNotTold()
    {
        var elsewhere = new CheckBox { Content = "Elsewhere" };
        var other = new Window { Width = 100, Height = 100, Content = elsewhere };
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(other);
        }

        ElementOf(new Button { Content = "Here" });
        Adamantium.UI.Core.Automation.AutomationEvents.Raise(elsewhere, Adamantium.UI.Core.Automation.AutomationEvent.FocusChanged);

        Assert.That(_told, Is.Empty);
    }

    [Test]
    public void LetGo_NothingMoreIsTold()
    {
        var check = new CheckBox { Content = "Bold" };
        _root.Children.Add(check);
        Settle();
        ElementOf(check);
        _accessibility.Dispose();

        check.IsChecked = true;

        Assert.That(_told, Is.Empty);
    }

    [Test]
    public void AFieldsText_IsReadByRange_AndSelected()
    {
        var box = new TextBox { Text = "Hello world" };
        _root.Children.Add(box);
        Settle();
        ((Adamantium.UI.Core.Automation.ITextProvider)box.GetAutomationPeer()
            .GetPattern(Adamantium.UI.Core.Automation.PatternId.Text)).Select(6, 5);
        var element = ElementOf(box);

        var count = ((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(element, Selector("accessibilityNumberOfCharacters"));
        var part = NSString.ToManaged(((delegate* unmanaged<IntPtr, IntPtr, NSRange, IntPtr>)ObjCRuntime.MessageSend)(
            element, Selector("accessibilityStringForRange:"), new NSRange(0, 5)));
        var selected = ((delegate* unmanaged<IntPtr, IntPtr, NSRange>)ObjCRuntime.MessageSend)(element, Selector("accessibilitySelectedTextRange"));
        var line = ((delegate* unmanaged<IntPtr, IntPtr, nint, nint>)ObjCRuntime.MessageSend)(element, Selector("accessibilityLineForIndex:"), 3);
        var lineRange = ((delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)ObjCRuntime.MessageSend)(
            element, Selector("accessibilityRangeForLine:"), 0);

        Assert.Multiple(() =>
        {
            Assert.That((int)count, Is.EqualTo(11));
            Assert.That(part, Is.EqualTo("Hello"));
            Assert.That(Text(element, "accessibilitySelectedText"), Is.EqualTo("world"));
            Assert.That(selected, Is.EqualTo(new NSRange(6, 5)));
            Assert.That((int)line, Is.EqualTo(0));
            Assert.That(lineRange, Is.EqualTo(new NSRange(0, 11)));
            Assert.That(Allowed(element, "accessibilityStringForRange:"), Is.True);
        });

        ((delegate* unmanaged<IntPtr, IntPtr, NSRange, void>)ObjCRuntime.MessageSend)(
            element, Selector("setAccessibilitySelectedTextRange:"), new NSRange(0, 5));
        Assert.That((box.SelectionStart, box.SelectionLength), Is.EqualTo((0, 5)));
    }

    [Test]
    public void AWholeCharacter_IsOne_AndNoLineComesAfterTheLast()
    {
        var box = new TextBox { Text = "a😀b" };
        _root.Children.Add(box);
        Settle();
        var element = ElementOf(box);

        var emoji = ((delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)ObjCRuntime.MessageSend)(
            element, Selector("accessibilityRangeForIndex:"), 2);
        var pastTheLast = ((delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)ObjCRuntime.MessageSend)(
            element, Selector("accessibilityRangeForLine:"), 5);
        var attributed = ((delegate* unmanaged<IntPtr, IntPtr, NSRange, IntPtr>)ObjCRuntime.MessageSend)(
            element, Selector("accessibilityAttributedStringForRange:"), new NSRange(1, 2));

        Assert.Multiple(() =>
        {
            Assert.That(emoji, Is.EqualTo(new NSRange(1, 2)), "both halves of the surrogate pair");
            Assert.That(pastTheLast.IsFound, Is.False);
            Assert.That(NSAttributedString.TextOf(attributed), Is.EqualTo("😀"));
        });
    }

    // A peer that tells it holds a password and would give its text: the bridge gives none.
    [Test]
    public void APasswordsText_IsNotTold_ThoughItsPeerHasIt()
    {
        var element = _accessibility.ElementOf(new PasswordTextPeer());

        Assert.Multiple(() =>
        {
            Assert.That((int)((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(element,
                Selector("accessibilityNumberOfCharacters")), Is.Zero);
            Assert.That(Allowed(element, "accessibilityStringForRange:"), Is.False);
            Assert.That(NSObject.Send(element, Selector("accessibilityValue")), Is.EqualTo(IntPtr.Zero));
        });
    }

    private sealed class PasswordTextPeer : Adamantium.UI.Core.Automation.AutomationPeer, Adamantium.UI.Core.Automation.ITextProvider
    {
        public override Adamantium.UI.Core.Automation.AutomationControlType ControlType =>
            Adamantium.UI.Core.Automation.AutomationControlType.Edit;
        public override string Name => "Password";
        public override string AutomationId => string.Empty;
        public override string HelpText => string.Empty;
        public override string ClassName => nameof(PasswordTextPeer);
        public override Rect BoundingRectangle => Rect.Empty;
        public override bool IsEnabled => true;
        public override bool IsOffscreen => false;
        public override bool HasKeyboardFocus => false;
        public override bool IsKeyboardFocusable => true;
        public override bool IsPassword => true;
        public override System.Collections.Generic.IReadOnlyList<Adamantium.UI.Core.Automation.AutomationPeer> GetChildren() => [];
        public override Adamantium.UI.Core.Automation.AutomationPeer GetParent() => null;
        public override void SetFocus() { }
        public string Text => "hunter2";
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public void Select(int start, int length) { }
        public int LineOf(int index) => 0;
        public (int Start, int End) LineRange(int line) => (0, Text.Length);
        public System.Collections.Generic.IReadOnlyList<Rect> Bounds(int start, int end) => [];
        public int IndexAt(PixelPoint screen) => 0;
        public void ScrollIntoView(int index) { }
    }

    [Test]
    public void APasswordsText_IsNotTold()
    {
        var box = new PasswordBox { Password = "hunter2" };
        _root.Children.Add(box);
        Settle();
        var element = ElementOf(box);

        Assert.Multiple(() =>
        {
            Assert.That((int)((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(element, Selector("accessibilityNumberOfCharacters")),
                Is.Zero);
            Assert.That(Allowed(element, "accessibilityStringForRange:"), Is.False);
            Assert.That(Allowed(element, "setAccessibilitySelectedTextRange:"), Is.False);
        });
    }

    [Test]
    public void AListsSelection_IsToldAndSet_EachItemByItsPlace()
    {
        var list = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.List,
            Adamantium.UI.Core.Automation.AutomationControlType.ListItem, "One", "Two", "Three");
        list.Children[1].Selected = true;
        var element = _accessibility.ElementOf(list);

        var selected = NSObject.Send(element, Selector("accessibilitySelectedChildren"));
        Assert.That(NSArray.CountOf(selected), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(NSArray.ObjectAt(selected, 0), Is.EqualTo(_accessibility.ElementOf(list.Children[1])));
            Assert.That((int)((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(
                _accessibility.ElementOf(list.Children[2]), Selector("accessibilityIndex")), Is.EqualTo(2));
            Assert.That(Allowed(element, "accessibilitySelectedChildren"), Is.True);
            Assert.That(Allowed(ElementOf(new Button()), "accessibilitySelectedChildren"), Is.False);
        });

        var choice = NSArray.Create([_accessibility.ElementOf(list.Children[2])]);
        NSObject.Send(element, Selector("setAccessibilitySelectedChildren:"), choice);
        Assert.That(list.Children[2].Selected && !list.Children[1].Selected, Is.True);
    }

    [Test]
    public void ATabGroup_HasItsTabs_AndTheOpenOneIsItsValue()
    {
        var tabs = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.Tab,
            Adamantium.UI.Core.Automation.AutomationControlType.TabItem, "First", "Second");
        tabs.Children[1].Selected = true;
        var element = _accessibility.ElementOf(tabs);
        var tabElements = NSObject.Send(element, Selector("accessibilityTabs"));
        Assert.That(NSArray.CountOf(tabElements), Is.EqualTo(2));
        var second = NSArray.ObjectAt(tabElements, 1);

        Assert.Multiple(() =>
        {
            Assert.That(Text(element, "accessibilityRole"), Is.EqualTo("AXTabGroup"));
            Assert.That(NSObject.Send(element, Selector("accessibilityValue")), Is.EqualTo(second), "the open tab");
            Assert.That(Number(second, "accessibilityValue"), Is.EqualTo(1));
            Assert.That(Number(NSArray.ObjectAt(tabElements, 0), "accessibilityValue"), Is.EqualTo(0));
        });
    }

    [Test]
    public void AnOutlinesRows_HaveTheirDepth_AndOpenAndShut()
    {
        var tree = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.Tree,
            Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "Parent");
        var parent = tree.Children[0];
        parent.Expandable = true;
        parent.Expanded = true;
        parent.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "Child") { Selectable = true });
        var element = _accessibility.ElementOf(tree);

        var rows = NSObject.Send(element, Selector("accessibilityRows"));
        Assert.That(NSArray.CountOf(rows), Is.EqualTo(2), "the parent and, open, its child");
        var parentRow = NSArray.ObjectAt(rows, 0);
        var childRow = NSArray.ObjectAt(rows, 1);

        Assert.Multiple(() =>
        {
            Assert.That((int)((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(childRow,
                Selector("accessibilityDisclosureLevel")), Is.EqualTo(1));
            Assert.That(Flag(parentRow, "isAccessibilityDisclosed"), Is.True);
            Assert.That(NSObject.Send(childRow, Selector("accessibilityDisclosedByRow")), Is.EqualTo(parentRow));
            Assert.That(NSArray.CountOf(NSObject.Send(parentRow, Selector("accessibilityDisclosedRows"))), Is.EqualTo(1));
        });

        NSObject.Send(parentRow, Selector("setAccessibilityDisclosed:"), false);
        Assert.That(parent.Expanded, Is.False);
    }

    [Test]
    public void AnOutlinesRow_StandsAtItsPlaceAmongTheRowsShown()
    {
        var tree = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.Tree,
            Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "A", "B");
        var a = tree.Children[0];
        a.Expandable = true;
        a.Expanded = true;
        a.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "A1") { Selectable = true });

        Assert.Multiple(() =>
        {
            Assert.That(IndexOf(_accessibility.ElementOf(a)), Is.EqualTo(0));
            Assert.That(IndexOf(_accessibility.ElementOf(a.Children[0])), Is.EqualTo(1));
            Assert.That(IndexOf(_accessibility.ElementOf(tree.Children[1])), Is.EqualTo(2));
        });
    }

    [Test]
    public void ATablesCell_IsACell_EditableOneToo_AndItsRowARow()
    {
        var grid = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.DataGrid, "Grid") { IsGrid = true };
        var row = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.DataItem, "Row")
            { InGrid = true, RowIndex = 4 };
        grid.Add(row);
        row.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.Text, "Read only")
            { InGrid = true, RowIndex = 4, ColumnIndex = 0 });
        var editable = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.DataItem, "Editable")
            { InGrid = true, RowIndex = 4, ColumnIndex = 1 };
        row.Add(editable);
        var rowElement = _accessibility.ElementOf(row);
        var cellElement = _accessibility.ElementOf(editable);

        Assert.Multiple(() =>
        {
            Assert.That(Text(rowElement, "accessibilityRole"), Is.EqualTo("AXRow"));
            Assert.That(Text(rowElement, "accessibilitySubrole"), Is.EqualTo("AXTableRow"));
            Assert.That(IndexOf(rowElement), Is.EqualTo(4));
            Assert.That(Allowed(rowElement, "accessibilityRowIndexRange"), Is.False, "a row is no cell");
            Assert.That(Text(cellElement, "accessibilityRole"), Is.EqualTo("AXCell"));
            Assert.That(Text(cellElement, "accessibilitySubrole"), Is.Null);
            Assert.That(IndexOf(cellElement), Is.EqualTo(1));
            Assert.That(Allowed(cellElement, "accessibilityRowIndexRange"), Is.True);
            Assert.That(NSArray.CountOf(NSObject.Send(_accessibility.ElementOf(grid), Selector("accessibilityRows"))),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void AListItem_HasNoValue_OfItsBeingSelected()
    {
        var list = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.List,
            Adamantium.UI.Core.Automation.AutomationControlType.ListItem, "One");
        list.Children[0].Selected = true;
        var item = _accessibility.ElementOf(list.Children[0]);

        Assert.That(Allowed(item, "accessibilityValue"), Is.False);
        Assert.That(NSObject.Send(item, Selector("accessibilityValue")), Is.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void SelectingSeveral_WhereOneIsTaken_SelectsTheFirstThatCanBe()
    {
        var list = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.List,
            Adamantium.UI.Core.Automation.AutomationControlType.ListItem, "One", "Two", "Three");
        var header = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.Header, "Header");
        list.Add(header);
        list.Children[0].Selected = true;
        var element = _accessibility.ElementOf(list);

        var choice = NSArray.Create([_accessibility.ElementOf(header), _accessibility.ElementOf(list.Children[2]),
            _accessibility.ElementOf(list.Children[1])]);
        NSObject.Send(element, Selector("setAccessibilitySelectedChildren:"), choice);

        Assert.That(list.Children.FindAll(child => child.Selected), Is.EqualTo(new[] { list.Children[2] }));

        list.SelectsSeveral = true;
        NSObject.Send(element, Selector("setAccessibilitySelectedChildren:"), NSArray.Create(
            [_accessibility.ElementOf(list.Children[0]), _accessibility.ElementOf(list.Children[1])]));
        Assert.That(list.Children.FindAll(child => child.Selected), Is.EqualTo(new[] { list.Children[0], list.Children[1] }),
            "the first alone, the rest added");
    }

    [Test]
    public void ARowOutOfATable_StandsAtItsPlace()
    {
        var pane = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.Pane, "Properties");
        pane.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.DataItem, "Name"));
        pane.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.DataItem, "Size"));
        var size = _accessibility.ElementOf(pane.Children[1]);

        Assert.Multiple(() =>
        {
            Assert.That(Text(size, "accessibilityRole"), Is.EqualTo("AXRow"));
            Assert.That(Allowed(size, "accessibilityIndex"), Is.True);
            Assert.That(IndexOf(size), Is.EqualTo(1));
        });
    }

    [Test]
    public void AnOutlinesRowUnderAShutOne_StandsAtItsPlaceAmongItsOwn()
    {
        var tree = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.Tree,
            Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "A");
        var a = tree.Children[0];
        a.Expandable = true;
        a.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "A1"));
        a.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, "A2"));

        Assert.That(IndexOf(_accessibility.ElementOf(a.Children[1])), Is.EqualTo(1));
    }

    [Test]
    public void AnOutlinesRowsPlace_IsFoundWithoutWalkingPastIt()
    {
        var tree = FakePeer.Container(Adamantium.UI.Core.Automation.AutomationControlType.Tree,
            Adamantium.UI.Core.Automation.AutomationControlType.TreeItem);
        for (var i = 0; i < 100; i++)
        {
            var branch = new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, $"Branch {i}")
                { Expandable = true, Expanded = true };
            tree.Add(branch);
            for (var j = 0; j < 100; j++)
                branch.Add(new FakePeer(Adamantium.UI.Core.Automation.AutomationControlType.TreeItem, $"Leaf {i}.{j}"));
        }

        var first = _accessibility.ElementOf(tree.Children[0].Children[0]);
        var last = _accessibility.ElementOf(tree.Children[99].Children[99]);
        IndexOf(first);
        IndexOf(last);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var firstPlace = IndexOf(first);
        var firstTook = timer.Elapsed;
        timer.Restart();
        var lastPlace = IndexOf(last);
        var lastTook = timer.Elapsed;
        TestContext.Out.WriteLine($"10 100 rows: the first's place in {firstTook.TotalMilliseconds:F3} ms, the last's in " +
                                  $"{lastTook.TotalMilliseconds:F3} ms");

        Assert.That(firstPlace, Is.EqualTo(1));
        Assert.That(lastPlace, Is.EqualTo(10_099));
    }

    [Test]
    public void QuietForAMinute_NothingIsTold_TillAskedAgain()
    {
        var clock = new ManualClock();
        var check = new CheckBox { Content = "Bold" };
        _root.Children.Add(check);
        Settle();
        using var quiet = Bridge(() => false, clock);
        var element = ElementOf(quiet, check);

        clock.Advance(TimeSpan.FromSeconds(61));
        check.IsChecked = true;
        check.IsChecked = false;
        Assert.That(_told, Is.Empty, "nobody asked for a minute and no assistive technology is on");

        quiet.Children();
        check.IsChecked = true;
        Assert.That(_told, Is.EqualTo(new[] { (element, "AXValueChanged") }), "asked again, it is told again");
    }

    [Test]
    public void QuietForAMinute_AnAssistiveTechnologyTurnedOn_IsToldWithinHalfAMinute()
    {
        var clock = new ManualClock();
        var on = false;
        var check = new CheckBox { Content = "Bold" };
        _root.Children.Add(check);
        Settle();
        using var bridge = Bridge(() => on, clock);
        var element = ElementOf(bridge, check);

        clock.Advance(TimeSpan.FromSeconds(61));
        check.IsChecked = true;
        Assert.That(_told, Is.Empty);

        on = true;
        clock.Advance(TimeSpan.FromSeconds(31));
        check.IsChecked = false;
        Assert.That(_told, Is.EqualTo(new[] { (element, "AXValueChanged") }), "Zoom turned on, nothing asked");
    }

    [Test]
    public void QuietForAMinute_ItIsStillTold_WhileAnAssistiveTechnologyIsOn()
    {
        var clock = new ManualClock();
        var check = new CheckBox { Content = "Bold" };
        _root.Children.Add(check);
        Settle();
        using var zoom = Bridge(() => true, clock);
        var element = ElementOf(zoom, check);

        clock.Advance(TimeSpan.FromSeconds(61));
        check.IsChecked = true;

        Assert.That(_told, Is.EqualTo(new[] { (element, "AXValueChanged") }));
    }

    [Test]
    public void WhetherAnAssistiveTechnologyIsOn_IsAnswered()
    {
        Assert.That(() => Adamantium.MacOS.AppKit.NSAccessibility.IsAssistiveTechnologyOn, Throws.Nothing,
            "VoiceOver and Switch Control asked of the workspace, Zoom of Universal Access");
    }

    private static int IndexOf(IntPtr element) =>
        (int)((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(element, Selector("accessibilityIndex"));

    // A peer the test builds - a container, its items, a row of an outline - with selection and openness of its own: the
    // bridge's answers without a control's template, which a test without a theme has not.
    private sealed class FakePeer(Adamantium.UI.Core.Automation.AutomationControlType type, string name)
        : Adamantium.UI.Core.Automation.AutomationPeer, Adamantium.UI.Core.Automation.ISelectionProvider,
            Adamantium.UI.Core.Automation.ISelectionItemProvider, Adamantium.UI.Core.Automation.IExpandCollapseProvider,
            Adamantium.UI.Core.Automation.IGridProvider, Adamantium.UI.Core.Automation.IGridItemProvider
    {
        public bool IsGrid { get; init; }
        public bool InGrid { get; init; }
        public int RowIndex { get; init; }
        public int ColumnIndex { get; init; }
        public System.Collections.Generic.List<FakePeer> Children { get; } = [];
        public FakePeer Parent { get; private set; }
        public bool IsContainer { get; init; }
        public bool Selectable { get; set; }
        public bool Selected { get; set; }
        public bool Expandable { get; set; }
        public bool Expanded { get; set; }

        public static FakePeer Container(Adamantium.UI.Core.Automation.AutomationControlType type,
            Adamantium.UI.Core.Automation.AutomationControlType itemType, params string[] items)
        {
            var container = new FakePeer(type, type.ToString()) { IsContainer = true };
            foreach (var item in items)
                container.Add(new FakePeer(itemType, item) { Selectable = true });
            return container;
        }

        public void Add(FakePeer child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public override Adamantium.UI.Core.Automation.AutomationControlType ControlType => type;
        public override string Name => name;
        public override string AutomationId => name;
        public override string HelpText => string.Empty;
        public override string ClassName => nameof(FakePeer);
        public override Rect BoundingRectangle => Rect.Empty;
        public override bool IsEnabled => true;
        public override bool IsOffscreen => false;
        public override bool HasKeyboardFocus => false;
        public override bool IsKeyboardFocusable => false;
        public override System.Collections.Generic.IReadOnlyList<Adamantium.UI.Core.Automation.AutomationPeer> GetChildren() => Children;
        public override Adamantium.UI.Core.Automation.AutomationPeer GetParent() => Parent;
        public override void SetFocus() { }

        public override object GetPattern(Adamantium.UI.Core.Automation.PatternId pattern) => pattern switch
        {
            Adamantium.UI.Core.Automation.PatternId.Selection when IsContainer => this,
            Adamantium.UI.Core.Automation.PatternId.SelectionItem when Selectable => this,
            Adamantium.UI.Core.Automation.PatternId.ExpandCollapse when Expandable => this,
            Adamantium.UI.Core.Automation.PatternId.Grid when IsGrid => this,
            Adamantium.UI.Core.Automation.PatternId.GridItem when InGrid => this,
            _ => null
        };

        public int RowCount => Children.Count;
        public int ColumnCount => Children.Count == 0 ? 0 : Children[0].Children.Count;
        public Adamantium.UI.Core.Automation.AutomationPeer GetItem(int row, int column) => Children[row].Children[column];

        public int Row => RowIndex;
        public int Column => ColumnIndex;
        public Adamantium.UI.Core.Automation.AutomationPeer ContainingGrid => Parent;

        public bool SelectsSeveral { get; set; }
        public bool CanSelectMultiple => SelectsSeveral;
        public void AddToSelection() => Selected = true;
        public bool IsSelectionRequired => false;
        public System.Collections.Generic.IReadOnlyList<Adamantium.UI.Core.Automation.AutomationPeer> GetSelection() =>
            Children.FindAll(child => child.Selected);

        public bool IsSelected => Selected;
        public Adamantium.UI.Core.Automation.AutomationPeer SelectionContainer => Parent;

        public void Select()
        {
            foreach (var sibling in Parent?.Children ?? [])
                sibling.Selected = false;
            Selected = true;
        }

        public Adamantium.UI.Core.Automation.ExpandCollapseState ExpandCollapseState => Expanded
            ? Adamantium.UI.Core.Automation.ExpandCollapseState.Expanded
            : Adamantium.UI.Core.Automation.ExpandCollapseState.Collapsed;

        public void Expand() => Expanded = true;
        public void Collapse() => Expanded = false;
    }

    private void Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    // As AppKit has an element: in answer to a question, which starts the listening.
    private IntPtr ElementOf(UIComponent component) => ElementOf(_accessibility, component);

    private static IntPtr ElementOf(MacOSAccessibility accessibility, UIComponent component)
    {
        var element = accessibility.ElementOf(component.GetAutomationPeer());
        Text(element, "accessibilityRole");
        return element;
    }

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
