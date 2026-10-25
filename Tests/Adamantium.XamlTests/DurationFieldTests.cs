using System;
using System.Globalization;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A length of time entered part by part, in the theme's own template: the largest part counts everything above
/// it, the others step round within themselves, parts not shown are kept, the sign, the bounds, and automation.</summary>
[TestFixture]
public class DurationFieldTests
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

    private DurationField Shown(DurationField field, bool focused = true)
    {
        field.Language ??= "ru-RU";
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 800, Height = 600, Content = field };
        _window.ApplyCurrentTheme();
        Settle();
        if (focused)
        {
            FocusManager.Focus(field, NavigationMethod.Tab);
            Settle();
        }

        return field;
    }

    private void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static bool Press(DurationField field, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        field.RaiseEvent(args);
        return args.Handled;
    }

    private static void Type(DurationField field, string text) =>
        field.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static string[] Texts(DurationField field) => [.. field.Segments.Select(s => s.Text)];

    private static TextBlock[] Literals(DurationField field) =>
        [.. ((Panel)field.GetTemplateChild("PART_Segments")).Children.OfType<TextBlock>()];

    private static string Shown(TextBlock[] literals) =>
        string.Concat(literals.Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text));

    // Shift as the key that brought the focus carried it, not as the keyboard's own state has it - an automation's
    // Shift+Tab came in at the first part.
    [Test]
    public void ComingInByShiftTab_StartsAtTheLastPart()
    {
        var field = Shown(new DurationField { Value = TimeSpan.FromSeconds(5025) }, focused: false);

        FocusManager.Focus(field, NavigationMethod.Tab, InputModifiers.LeftShift);
        Settle();

        Assert.That(field.ActiveSegment, Is.SameAs(field.Segments[^1]));
    }

    [Test]
    public void ThePartBeingTyped_IsFilledInTheAccent_ByDefault()
    {
        var field = Shown(new DurationField { Value = TimeSpan.FromMinutes(5) });
        var chrome = (Adamantium.UI.Controls.Decorators.Border)field.ActiveSegment.GetTemplateChild("Chrome");

        Assert.That(chrome.Background is Adamantium.UI.Core.Media.SolidColorBrush { Color.A: > 0 }, "filled");
    }

    [Test]
    public void TooNarrowForItsParts_TheFieldSlidesToThePartBeingTyped()
    {
        var field = Shown(new DurationField { Width = 40, Value = TimeSpan.FromSeconds(5025) });
        var row = (FieldSegmentPanel)field.GetTemplateChild("PART_Segments");
        var last = field.Segments[^1];
        if (last.Bounds.Width >= row.RenderSize.Width)
        {
            field.Width += last.Bounds.Width - row.RenderSize.Width + 4;
            Settle();
            last = field.Segments[^1];
        }

        Assert.That(last.Bounds.Right, Is.GreaterThan(row.RenderSize.Width), "the parts do not fit");

        for (var i = 0; i < field.Segments.Count && field.ActiveSegment != last; i++)
        {
            Press(field, Key.RightArrow);
            Settle();
        }

        Assert.That(field.ActiveSegment, Is.SameAs(last));
        Assert.That(last.Bounds.X, Is.GreaterThanOrEqualTo(-0.5));
        Assert.That(last.Bounds.Right, Is.LessThanOrEqualTo(row.RenderSize.Width + 0.5), "the part being typed is in view");
    }

    [Test]
    public void HoursMinutesAndSeconds_AreTheDefault_EmptyUntilEntered()
    {
        var field = Shown(new DurationField(), focused: false);

        Assert.Multiple(() =>
        {
            Assert.That(field.Segments.Select(s => s.Part),
                Is.EqualTo(new[] { DurationParts.Hours, DurationParts.Minutes, DurationParts.Seconds }));
            Assert.That(field.Value, Is.Null);
            Assert.That(field.Segments.All(s => s.IsEmpty));
            Assert.That(Texts(field), Is.EqualTo(new[] { "hh", "mm", "ss" }).Or.EqualTo(new[] { "чч", "мм", "сс" }));
            Assert.That(Shown(Literals(field)), Is.EqualTo("::"), "the sign stays hidden");
        });
    }

    [Test]
    public void TheLargestPart_CountsEverythingAboveIt()
    {
        var field = Shown(new DurationField { Value = new TimeSpan(1, 6, 5, 9) }, focused: false);

        Assert.That(Texts(field), Is.EqualTo(new[] { "30", "05", "09" }));
    }

    [Test]
    public void TypingTheLargestPart_TakesMoreThanTwoDigits_UntilMovedOn()
    {
        var field = Shown(new DurationField());

        Type(field, "125");
        Assert.That(field.ActiveSegment.Part, Is.EqualTo(DurationParts.Hours), "a length of hours has no ceiling of 23");

        Press(field, Key.Tab);
        Type(field, "30");
        Type(field, "7");

        Assert.Multiple(() =>
        {
            Assert.That(field.Value, Is.EqualTo(new TimeSpan(125, 30, 7)));
            Assert.That(Texts(field), Is.EqualTo(new[] { "125", "30", "07" }));
        });
    }

    [Test]
    public void ASmallerPart_StepsRoundWithinItself()
    {
        var field = Shown(new DurationField { Value = new TimeSpan(2, 59, 0) });
        Press(field, Key.RightArrow);

        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(new TimeSpan(2, 0, 0)), "59 minutes go round to 0 and the hours stay");
    }

    [Test]
    public void Days_AreShownWithTheirWord()
    {
        var field = Shown(new DurationField
        {
            Parts = DurationParts.Days | DurationParts.Hours | DurationParts.Minutes,
            Value = new TimeSpan(2, 5, 30, 0),
        }, focused: false);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(field), Is.EqualTo(new[] { "2", "05", "30" }));
            Assert.That(Shown(Literals(field)), Is.EqualTo("d:").Or.EqualTo("д:"), "the word for days after them, no separator");
            Assert.That(field.Segments[0].GetAutomationPeer().Name, Is.EqualTo("Days").Or.EqualTo("Дни"));
        });
    }

    [Test]
    public void Milliseconds_FollowTheDecimalSeparator()
    {
        var field = Shown(new DurationField
        {
            Parts = DurationParts.Seconds | DurationParts.Milliseconds,
            Value = TimeSpan.FromMilliseconds(61250),
        }, focused: false);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(field), Is.EqualTo(new[] { "61", "250" }));
            Assert.That(Shown(Literals(field)), Is.EqualTo(CultureInfo.GetCultureInfo("ru-RU").NumberFormat.NumberDecimalSeparator));
        });
    }

    [Test]
    public void APartNotShown_KeepsWhatTheValueHad()
    {
        var field = Shown(new DurationField
        {
            Parts = DurationParts.Hours | DurationParts.Minutes,
            Value = new TimeSpan(0, 1, 2, 3, 500),
        });

        Type(field, "4");
        Press(field, Key.Tab);

        Assert.That(field.Value, Is.EqualTo(new TimeSpan(0, 4, 2, 3, 500)), "the seconds and milliseconds stay");
    }

    [Test]
    public void AMinus_MakesItNegative_AndAPlusTakesItBack()
    {
        var field = Shown(new DurationField { Value = new TimeSpan(0, 1, 30) });

        Type(field, "-");

        Assert.Multiple(() =>
        {
            Assert.That(field.Value, Is.EqualTo(-new TimeSpan(0, 1, 30)));
            Assert.That(Texts(field), Is.EqualTo(new[] { "00", "01", "30" }), "the parts are the length, the sign stands apart");
            Assert.That(Shown(Literals(field)), Is.EqualTo("−::"));
        });

        Type(field, "+");
        Assert.That(field.Value, Is.EqualTo(new TimeSpan(0, 1, 30)));
    }

    [Test]
    public void AMinimumOfZero_TakesNoMinus()
    {
        var field = Shown(new DurationField { Minimum = TimeSpan.Zero, Value = new TimeSpan(0, 1, 30) });

        Type(field, "-");

        Assert.That(field.Value, Is.EqualTo(new TimeSpan(0, 1, 30)));
    }

    [Test]
    public void AValuePastTheMaximum_IsHeldAtIt()
    {
        var field = Shown(new DurationField { Maximum = TimeSpan.FromHours(1), Value = TimeSpan.Zero });

        Type(field, "5");
        Press(field, Key.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(field.Value, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(Texts(field), Is.EqualTo(new[] { "01", "00", "00" }));
        });
    }

    [Test]
    public void TheLargestPart_HoldsMoreThanAnIntCounts()
    {
        var month = TimeSpan.FromDays(30);
        var field = Shown(new DurationField { Parts = DurationParts.Milliseconds, Value = month });

        Press(field, Key.UpArrow);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(field), Is.EqualTo(new[] { "2592000001" }));
            Assert.That(field.Value, Is.EqualTo(month + TimeSpan.FromMilliseconds(1)));
        });
    }

    [Test]
    public void AValueWrittenFromOutside_IsHeldBetweenTheBounds()
    {
        var field = Shown(new DurationField { Minimum = TimeSpan.Zero, Maximum = TimeSpan.FromHours(2) }, focused: false);

        field.Value = TimeSpan.FromHours(-1);
        Assert.That(field.Value, Is.EqualTo(TimeSpan.Zero));

        ((DurationFieldAutomationPeer)field.GetAutomationPeer()).SetValue("05:00:00");
        Assert.That(field.Value, Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [Test]
    public void TheWordForDays_WaitsForTheDays_ItsRoomKept()
    {
        var field = Shown(new DurationField { Parts = DurationParts.Days | DurationParts.Hours }, focused: false);
        var unit = Literals(field).First(t => t.Classes.Contains("DurationFieldDayUnit"));

        Assert.That(unit.Visibility, Is.EqualTo(Visibility.Hidden));

        field.Value = TimeSpan.FromDays(3);
        Assert.That(unit.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Milliseconds_AfterAnythingButSeconds_FollowTheTimeSeparator()
    {
        var field = Shown(new DurationField
        {
            Parts = DurationParts.Hours | DurationParts.Milliseconds,
            Value = new TimeSpan(0, 5, 0, 0, 250),
        }, focused: false);

        Assert.That(Shown(Literals(field)), Is.EqualTo(CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.TimeSeparator),
            "05,250 would read as hours and a fraction of one");
    }

    [Test]
    public void NoPartsNamed_ShowsHoursMinutesAndSeconds()
    {
        var field = Shown(new DurationField { Parts = DurationParts.None, Value = new TimeSpan(1, 2, 3) }, focused: false);

        Assert.That(Texts(field), Is.EqualTo(new[] { "01", "02", "03" }));
    }

    [Test]
    public void EmptyingAPart_EmptiesTheValue()
    {
        var field = Shown(new DurationField { Value = new TimeSpan(1, 2, 3) });

        Press(field, Key.Delete);

        Assert.Multiple(() =>
        {
            Assert.That(field.Value, Is.Null);
            Assert.That(field.Segments[0].IsEmpty);
        });
    }

    [Test]
    public void Automation_ReadsAndWritesTheLength_AndEachPartIsASpinner()
    {
        var field = Shown(new DurationField { Value = new TimeSpan(1, 2, 3) }, focused: false);
        var peer = (DurationFieldAutomationPeer)field.GetAutomationPeer();

        Assert.That(peer.Value, Is.EqualTo("01:02:03"));
        peer.SetValue("-00:00:05");
        Assert.That(field.Value, Is.EqualTo(TimeSpan.FromSeconds(-5)));

        var minutes = (FieldSegmentAutomationPeer)peer.GetChildren()[1];
        minutes.SetValue(45);

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Edit));
            Assert.That(minutes.ControlType, Is.EqualTo(AutomationControlType.Spinner));
            Assert.That(minutes.Maximum, Is.EqualTo(59));
            Assert.That(field.Value, Is.EqualTo(-new TimeSpan(0, 45, 5)));
            Assert.Throws<FormatException>(() => peer.SetValue("long"));
        });
    }
}
