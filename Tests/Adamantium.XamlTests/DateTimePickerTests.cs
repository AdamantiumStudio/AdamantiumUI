using System;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The pickers, in the theme's own template: a date and time field with a button that opens a calendar, the
/// times of a day - a list, columns or a clock face - or both; what is picked lands in the field and keeps the rest of
/// its value, and the keyboard opens, walks and closes the choice.</summary>
[TestFixture]
public class DateTimePickerTests
{
    private FakeApp _app;
    private Window _window;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    [TearDown]
    public void LetGo() => FocusManager.ResetFocus();

    private T Shown<T>(T picker, bool focused = true) where T : DateTimePickerBase
    {
        picker.HorizontalAlignment = HorizontalAlignment.Left;
        picker.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 800, Height = 600, Content = picker };
        _window.ApplyCurrentTheme();
        Settle();
        if (focused)
        {
            FocusManager.Focus(picker, NavigationMethod.Tab);
            Settle();
        }

        return picker;
    }

    private void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
            _window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size(_window.Width, _window.Height));
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static bool Press(IInputComponent target, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
        if (args.Handled)
        {
            return true;
        }

        args.RoutedEvent = Keyboard.KeyDownEvent;
        target.RaiseEvent(args);
        return args.Handled;
    }

    [Test]
    public void APicker_IsAFieldTheKeyboardTypesInto()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU" });

        picker.RaiseEvent(new TextInputEventArgs("19102026") { RoutedEvent = Keyboard.TextInputEvent });

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
    }

    [TestCase(Key.F4, InputModifiers.None)]
    [TestCase(Key.DownArrow, InputModifiers.LeftAlt)]
    public void TheKeyboard_OpensTheCalendar_OnTheValue(Key key, InputModifiers modifiers)
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19, 14, 5, 0) });

        Assert.That(Press(picker, key, modifiers), Is.True);
        Settle();

        Assert.That(picker.IsDropDownOpen);
        Assert.That(picker.DropDownCalendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 19)));
        Assert.That(picker.DropDownCalendar.DisplayDate.Month, Is.EqualTo(10));
    }

    [Test]
    public void ADayPicked_KeepsTheTime()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19, 14, 5, 0) });
        picker.IsDropDownOpen = true;
        Settle();

        picker.DropDownCalendar.SelectedDate = new DateTime(2026, 10, 25);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 25, 14, 5, 0)));
    }

    [Test]
    public void EnterInTheCalendar_PicksAndCloses()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });
        picker.IsDropDownOpen = true;
        Settle();
        FocusManager.Focus(picker.DropDownCalendar, NavigationMethod.Tab);

        Press(picker.DropDownCalendar, Key.RightArrow);
        Press(picker.DropDownCalendar, Key.Enter);
        Settle();

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 20)));
        Assert.That(picker.IsDropDownOpen, Is.False);
    }

    [Test]
    public void Escape_ClosesWithoutPicking()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });
        picker.IsDropDownOpen = true;
        Settle();

        Press(picker.DropDownCalendar, Key.Escape);

        Assert.That(picker.IsDropDownOpen, Is.False);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
    }

    [Test]
    public void TheListOfTimes_IsAStepApart_AndATimePickedKeepsTheDay()
    {
        var picker = Shown(new TimePicker
        {
            Language = "ru-RU", TimeInterval = TimeSpan.FromMinutes(30), Value = new DateTime(2026, 10, 19, 9, 0, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();
        var list = (ListBox)picker.DropDownContent;

        Assert.That(list.Items.Count, Is.EqualTo(48));
        Assert.That(list.SelectedItem, Is.EqualTo("09:00"));
        list.SelectedIndex = 21;
        Press(list, Key.Enter);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19, 10, 30, 0)));
        Assert.That(picker.IsDropDownOpen, Is.False);
    }

    [Test]
    public void TheListOfTimes_SpeaksTheLanguage()
    {
        var picker = Shown(new TimePicker { Language = "en-US", Value = new DateTime(2026, 10, 19, 14, 0, 0) });
        picker.IsDropDownOpen = true;
        Settle();
        var list = (ListBox)picker.DropDownContent;

        Assert.That(list.SelectedItem, Is.EqualTo("2:00 PM"));
    }

    [Test]
    public void Columns_TakeTheHourMinuteAndPeriod()
    {
        var picker = Shown(new TimePicker
        {
            Language = "en-US", PopupStyle = TimePickerPopupStyle.Columns, TimeInterval = TimeSpan.FromMinutes(5),
            Value = new DateTime(2026, 10, 19, 9, 10, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();
        var lists = Descendants<ListBox>(picker.DropDownContent).ToList();

        Assert.That(lists, Has.Count.EqualTo(3));
        Assert.That(lists[0].SelectedItem, Is.EqualTo("9"));
        Assert.That(lists[1].SelectedItem, Is.EqualTo("10"));
        lists[0].SelectedIndex = 2;
        lists[1].SelectedIndex = 9;
        lists[2].SelectedIndex = 1;
        Press(lists[0], Key.Enter);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19, 15, 45, 0)));
    }

    [Test]
    public void TheDial_StepsTheHourThenTheMinutes_AndEnterTakesThem()
    {
        var picker = Shown(new TimePicker
        {
            Language = "ru-RU", PopupStyle = TimePickerPopupStyle.Dial, Value = new DateTime(2026, 10, 19, 14, 5, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();
        var face = Descendants<Adamantium.UI.Controls.Panels.Canvas>(picker.DropDownContent).Single();

        Press(face, Key.UpArrow);
        Press(face, Key.Enter);
        Press(face, Key.DownArrow);
        Press(face, Key.Enter);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19, 15, 4, 0)));
        Assert.That(picker.IsDropDownOpen, Is.False);
    }

    [Test]
    public void DateAndTime_ADayKeepsItOpen_ATimeCloses()
    {
        var picker = Shown(new DateTimePicker
        {
            Language = "ru-RU", TimeInterval = TimeSpan.FromHours(1), Value = new DateTime(2026, 10, 19, 14, 5, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();

        picker.DropDownCalendar.SelectedDate = new DateTime(2026, 10, 21);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 21, 14, 5, 0)));
        Assert.That(picker.IsDropDownOpen, "a day picked leaves the times to pick");

        var times = Descendants<ListBox>(picker.DropDownContent).Single();
        times.SelectedIndex = 9;
        Press(times, Key.Enter);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 21, 9, 0, 0)));
        Assert.That(picker.IsDropDownOpen, Is.False);
    }

    [Test]
    public void OpenBeforeItsTemplate_ItShowsTheValue()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2020, 5, 7), IsDropDownOpen = true },
            focused: false);

        Assert.That(picker.DropDownCalendar.SelectedDate, Is.EqualTo(new DateTime(2020, 5, 7)));
        Assert.That(FocusManager.Focused, Is.SameAs(picker.DropDownCalendar), "the keyboard goes into the calendar");
    }

    [Test]
    public void AltUpInTheCalendar_ClosesAndLeavesTheValue()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });
        Press(picker, Key.F4);
        Settle();

        Press(picker.DropDownCalendar, Key.UpArrow, InputModifiers.LeftAlt);

        Assert.That(picker.IsDropDownOpen, Is.False);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
    }

    [Test]
    public void ColumnsWithAnIntervalUnderAMinute_StepByMinutes()
    {
        var picker = Shown(new TimePicker
        {
            Language = "ru-RU", PopupStyle = TimePickerPopupStyle.Columns, TimeInterval = TimeSpan.FromSeconds(30),
        });

        Assert.That(() => { picker.IsDropDownOpen = true; Settle(); }, Throws.Nothing);
        Assert.That(Descendants<ListBox>(picker.DropDownContent).ElementAt(1).Items.Count, Is.EqualTo(60));
    }

    [Test]
    public void ALetterHInQuotes_IsNoTwelveHourClock()
    {
        var picker = Shown(new TimePicker
        {
            Language = "fr-CA", PopupStyle = TimePickerPopupStyle.Columns, Value = new DateTime(2026, 10, 19, 14, 0, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();
        var lists = Descendants<ListBox>(picker.DropDownContent).ToList();

        Assert.That(lists[0].Items.Count, Is.EqualTo(24));
        Assert.That(lists[2].Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ADayPicked_LeavesTheTimeBeingSetOnTheDial()
    {
        var picker = Shown(new DateTimePicker
        {
            Language = "ru-RU", PopupStyle = TimePickerPopupStyle.Dial, Value = new DateTime(2026, 10, 19, 14, 5, 0),
        });
        picker.IsDropDownOpen = true;
        Settle();
        var face = Descendants<Adamantium.UI.Controls.Panels.Canvas>(picker.DropDownContent).Single();

        Press(face, Key.UpArrow);
        picker.DropDownCalendar.SelectedDate = new DateTime(2026, 10, 21);
        Press(face, Key.Enter);
        Press(face, Key.Enter);

        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 10, 21, 15, 5, 0)));
    }

    [Test]
    public void APickerText_IsNotSelectable_AsTheFieldsIsNot()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });

        Assert.That(picker.GetValue<bool>(Adamantium.UI.Controls.Text.TextBlock.IsTextSelectionEnabledProperty), Is.False);
    }

    [Test]
    public void Automation_SeesAComboBoxThatExpands()
    {
        var picker = Shown(new DatePicker { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });
        var peer = (DateTimePickerAutomationPeer)picker.GetAutomationPeer();

        Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.ComboBox));
        Assert.That(peer.Value, Is.EqualTo("2026-10-19T00:00:00"));
        peer.Expand();
        Assert.That(peer.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
        peer.Collapse();
        Assert.That(picker.IsDropDownOpen, Is.False);
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(IUIComponent root) where T : class
    {
        if (root == null)
        {
            yield break;
        }

        foreach (var child in root.VisualChildren)
        {
            if (child is T found)
            {
                yield return found;
            }

            foreach (var deeper in Descendants<T>(child))
            {
                yield return deeper;
            }
        }
    }
}
