using System;
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

/// <summary>A date and time entered part by part, in the theme's own template: the parts in the language's order and
/// form, digits that fill a part and move on, arrows that step a part round within itself, a value only once every part
/// is entered, other calendars, and automation.</summary>
[TestFixture]
public class DateTimeFieldTests
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

    private DateTimeField Shown(DateTimeField field, bool focused = true)
    {
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

    private static bool Press(DateTimeField field, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        field.RaiseEvent(args);
        return args.Handled;
    }

    private static void Type(DateTimeField field, string text) =>
        field.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static DateTimeSegmentKind[] Kinds(DateTimeField field) => [.. field.Segments.Select(s => s.Kind)];

    private static string[] Texts(DateTimeField field) => [.. field.Segments.Select(s => s.Text)];

    private static string Literals(DateTimeField field) => string.Concat(
        ((Panel)field.GetTemplateChild("PART_Segments")).Children.OfType<TextBlock>().Select(t => t.Text));

    [Test]
    public void TheShortDate_IsInTheLanguagesOrder()
    {
        var russian = Shown(new DateTimeField { Language = "ru-RU" }, focused: false);
        Assert.That(Kinds(russian), Is.EqualTo(new[] { DateTimeSegmentKind.Day, DateTimeSegmentKind.Month, DateTimeSegmentKind.Year }));
        Assert.That(Literals(russian), Is.EqualTo(".."));

        var american = Shown(new DateTimeField { Language = "en-US" }, focused: false);
        Assert.That(Kinds(american), Is.EqualTo(new[] { DateTimeSegmentKind.Month, DateTimeSegmentKind.Day, DateTimeSegmentKind.Year }));
        Assert.That(Literals(american), Is.EqualTo("//"));
    }

    [Test]
    public void EmptyParts_ShowTheirPlaceholders_AndTheValueIsNull()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" }, focused: false);

        Assert.That(field.Value, Is.Null);
        Assert.That(field.Segments.All(s => s.IsEmpty));
        Assert.That(Texts(field), Is.EqualTo(new[] { "dd", "mm", "yyyy" }).Or.EqualTo(new[] { "дд", "мм", "гггг" }));
    }

    [Test]
    public void Typing_FillsEachPartAndMovesOn_UntilTheValueIsWhole()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Type(field, "19");
        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Month), "a full day moves on");
        Assert.That(field.Value, Is.Null, "nothing until every part is entered");

        Type(field, "10");
        Type(field, "2026");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
        Assert.That(Texts(field), Is.EqualTo(new[] { "19", "10", "2026" }));
    }

    [Test]
    public void ADigitNoSecondCanFollow_MovesOnAtOnce()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Type(field, "4");

        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Month), "no day is forty-something");
        Assert.That(Texts(field)[0], Is.EqualTo("04"));
    }

    [Test]
    public void TwoDigitsOfAYear_LeftBehind_AreTheCenturysYear()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2000, 1, 1) });
        Press(field, Key.RightArrow);
        Press(field, Key.RightArrow);

        Type(field, "26");
        Assert.That(field.Value, Is.EqualTo(new DateTime(2000, 1, 1)), "a year typed in part is not the year yet");
        Press(field, Key.LeftArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 1, 1)));
    }

    [Test]
    public void UpOnTheLastDay_GoesRoundToTheFirst_InTheSameMonth()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 1, 31) });

        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 1, 1)));
    }

    [Test]
    public void DownOnJanuary_GoesRoundToDecember_InTheSameYear()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 1, 15) });
        Press(field, Key.RightArrow);

        Press(field, Key.DownArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 12, 15)));
    }

    [Test]
    public void AnArrowOnAnEmptyPart_StartsItAtNow()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Press(field, Key.UpArrow);

        Assert.That(field.Segments[0].Text, Is.EqualTo(DateTime.Today.Day.ToString("00")));
        Assert.That(field.Value, Is.Null);
    }

    [Test]
    public void BackspaceEmptiesThePart_AndTheValue()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 10, 19) });

        Press(field, Key.BackSpace);

        Assert.That(field.Value, Is.Null);
        Assert.That(field.Segments[0].IsEmpty);
        Assert.That(field.Segments[1].Text, Is.EqualTo("10"), "the other parts stay");
    }

    [Test]
    public void ADayPastTheMonthsEnd_IsItsLastDay()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Type(field, "31");
        Type(field, "02");
        Type(field, "2026");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(Texts(field)[0], Is.EqualTo("28"));
    }

    [Test]
    public void TabMovesThroughTheParts_AndLeavesAfterTheLast()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Assert.That(Press(field, Key.Tab), Is.True);
        Assert.That(Press(field, Key.Tab), Is.True);
        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Year));
        Assert.That(Press(field, Key.Tab), Is.False, "the last part lets the focus go on");
        Assert.That(Press(field, Key.Tab, InputModifiers.LeftShift), Is.True);
        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Month));
    }

    [Test]
    public void ASeparator_MovesToTheNextPart()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Type(field, "1.");

        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Month));
        Assert.That(Texts(field)[0], Is.EqualTo("01"));
    }

    [Test]
    public void OnlyTheFocusedFieldLightsAPart()
    {
        var field = new DateTimeField { Language = "ru-RU" };
        var other = new TextBox();
        _window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { field, other } } };
        _window.ApplyCurrentTheme();
        Settle();
        Assert.That(field.ActiveSegment, Is.Null);
        Assert.That(field.Segments.Any(s => s.IsActive), Is.False);

        FocusManager.Focus(field, NavigationMethod.Tab);
        Assert.That(field.Segments[0].IsActive);

        FocusManager.Focus(other, NavigationMethod.Tab);
        Assert.That(field.Segments.Any(s => s.IsActive), Is.False);
    }

    [Test]
    public void ADateField_KeepsTheTimeOfItsValue()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 10, 19, 14, 5, 0) });

        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 20, 14, 5, 0)));
    }

    [Test]
    public void ATwelveHourTime_TakesTheHourMinutesAndPeriod()
    {
        var field = Shown(new DateTimeField
        {
            Language = "en-US", Format = DateTimeFieldFormat.ShortTime, Value = new DateTime(2026, 10, 19, 9, 0, 0),
        });
        Assert.That(Kinds(field),
            Is.EqualTo(new[] { DateTimeSegmentKind.Hour, DateTimeSegmentKind.Minute, DateTimeSegmentKind.Period }));
        Assert.That(Texts(field), Is.EqualTo(new[] { "9", "00", "AM" }));

        Type(field, "2");
        Type(field, "05");
        Type(field, "p");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 19, 14, 5, 0)), "the date stays");
    }

    [Test]
    public void ATwentyFourHourTime_ShowsTheLanguagesSeparator()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ru-RU", Format = DateTimeFieldFormat.LongTime, Value = new DateTime(2026, 10, 19, 21, 7, 3),
        }, focused: false);

        Assert.That(Texts(field), Is.EqualTo(new[] { "21", "07", "03" }));
        Assert.That(Literals(field), Is.EqualTo("::"));
    }

    [Test]
    public void ACustomPattern_NamesTheMonth_AndALetterPicksIt()
    {
        var field = Shown(new DateTimeField
        {
            Language = "en-US", Format = DateTimeFieldFormat.Custom, CustomFormat = "dd MMM yyyy",
            Value = new DateTime(2026, 1, 5),
        });
        Assert.That(Texts(field), Is.EqualTo(new[] { "05", "Jan", "2026" }));

        Press(field, Key.RightArrow);
        Type(field, "o");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 5)));
        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Year), "one month starts so: it moves on");
    }

    [Test]
    public void JapaneseEras_AreAPartOfTheirOwn()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(2026, 10, 19),
        }, focused: false);

        Assert.That(Kinds(field)[0], Is.EqualTo(DateTimeSegmentKind.Era));
        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Year).Text, Is.EqualTo("8"),
            "2026 is the eighth year of Reiwa");
    }

    [Test]
    public void TheHijriCalendar_CountsTheParts()
    {
        var field = Shown(new DateTimeField
        {
            Language = "en-US", CalendarSystem = CalendarSystem.UmAlQura, Value = new DateTime(2026, 10, 19),
        }, focused: false);
        var hijri = new System.Globalization.UmAlQuraCalendar();

        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Year).Text,
            Is.EqualTo(hijri.GetYear(new DateTime(2026, 10, 19)).ToString()));
    }

    [Test]
    public void AValueSetFromOutside_FillsTheParts_AndNullEmptiesThem()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" }, focused: false);

        field.Value = new DateTime(2026, 3, 8);
        Assert.That(Texts(field), Is.EqualTo(new[] { "08", "03", "2026" }));

        field.Value = null;
        Assert.That(field.Segments.All(s => s.IsEmpty));
    }

    [TestCase("ru-RU", "19.10.2026")]
    [TestCase("en-US", "10/19/2026")]
    public void AWholeDateTypedWithItsSeparators_LandsPartByPart(string language, string typed)
    {
        var field = Shown(new DateTimeField { Language = language });

        Type(field, typed);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
    }

    [Test]
    public void ADayTheMonthLacks_IsKept_UntilAMonthHasIt()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 2, 15) });

        Type(field, "29");
        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 2, 28)), "2026 has no 29 February");
        Press(field, Key.RightArrow);
        Type(field, "2028");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2028, 2, 29)));
    }

    [Test]
    public void TheDayTyped_ComesBackInALongerMonth()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 1, 31) });
        Press(field, Key.RightArrow);

        Press(field, Key.UpArrow);
        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 2, 28)));
        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 3, 31)));
    }

    [TestCase("ar-SA", CalendarSystem.Language)]
    [TestCase("fa-IR", CalendarSystem.Language)]
    [TestCase("ja-JP", CalendarSystem.Japanese)]
    [TestCase("he-IL", CalendarSystem.Hebrew)]
    public void AValueTheCalendarCannotCount_IsShownAtItsFirstDay_AndLeftAsItIs(string language, CalendarSystem system)
    {
        var field = Shown(new DateTimeField { Language = language, CalendarSystem = system }, focused: false);

        Assert.That(() => field.Value = DateTime.MinValue, Throws.Nothing);
        Assert.That(field.Value, Is.EqualTo(DateTime.MinValue), "showing a value does not change it");
        Assert.That(field.Segments.Where(s => s.Kind != DateTimeSegmentKind.DayOfWeek).All(s => !s.IsEmpty));
    }

    [Test]
    public void EmptyingAndRetypingATime_KeepsTheDate()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ru-RU", Format = DateTimeFieldFormat.ShortTime, Value = new DateTime(2020, 1, 1, 9, 30, 0),
        });

        Press(field, Key.BackSpace);
        Type(field, "10");

        Assert.That(field.Value, Is.EqualTo(new DateTime(2020, 1, 1, 10, 30, 0)));
    }

    [Test]
    public void AnEdit_KeepsTheValuesKind()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ru-RU", Value = DateTime.SpecifyKind(new DateTime(2026, 10, 19, 8, 0, 0), DateTimeKind.Utc),
        });

        Press(field, Key.UpArrow);

        Assert.That(field.Value?.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void ASourceThatCoerces_IsWhatTheFieldShows()
    {
        var source = new CappedDate { Date = new DateTime(2026, 10, 19) };
        var field = new DateTimeField { Language = "ru-RU", DataContext = source };
        field.SetBinding(DateTimeField.ValueProperty, new Adamantium.UI.Core.Data.Binding(nameof(CappedDate.Date)));
        Shown(field);
        Press(field, Key.RightArrow);
        Press(field, Key.RightArrow);

        Type(field, "2030");
        Settle();

        Assert.That(source.Date, Is.EqualTo(new DateTime(2026, 12, 31)));
        Assert.That(field.Segments.Last().Text, Is.EqualTo("2026"));
    }

    [Test]
    public void SteppingTheEra_KeepsTheYearInIt()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1975, 6, 1),
        });
        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Era));

        Press(field, Key.UpArrow);

        var japanese = new System.Globalization.JapaneseCalendar();
        Assert.That(field.Value, Is.Not.Null);
        Assert.That(japanese.GetEra(field.Value.Value), Is.EqualTo(4), "Heisei");
        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Year).Text,
            Is.EqualTo(japanese.GetYear(field.Value.Value).ToString()), "the year shown is the value's");
    }

    [TestCase(2019, 4, 30, 1, 2019, 1, 30)]
    [TestCase(2019, 12, 15, 1, 2019, 5, 15)]
    public void AMonthGoesRound_TheMonthsTheErasYearHas(int year, int month, int day, int by, int toYear, int toMonth, int toDay)
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(year, month, day),
        });
        var era = new System.Globalization.JapaneseCalendar().GetEra(new DateTime(year, month, day));
        Press(field, Key.RightArrow);
        Press(field, Key.RightArrow);

        Press(field, by > 0 ? Key.UpArrow : Key.DownArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(toYear, toMonth, toDay)),
            "Heisei 31 has January to April, Reiwa 1 May to December");
        Assert.That(new System.Globalization.JapaneseCalendar().GetEra(field.Value.Value), Is.EqualTo(era));
    }

    [Test]
    public void MovingThroughTheParts_DoesNotRewriteAValueOutOfTheCalendarsRange()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1800, 1, 1),
        });

        for (var i = 0; i < 6; i++)
        {
            Press(field, Key.Tab);
        }

        Assert.That(field.Value, Is.EqualTo(new DateTime(1800, 1, 1)));
    }

    [Test]
    public void ATimeOfADateOutOfTheCalendarsRange_IsItsOwn()
    {
        var field = Shown(new DateTimeField
        {
            Language = "fa-IR", Format = DateTimeFieldFormat.ShortTime, Value = new DateTime(1, 1, 1, 9, 30, 0),
        });

        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Hour).Text, Does.EndWith("9"));
        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Minute).Text, Is.EqualTo("30"));
    }

    [Test]
    public void EditingTheTimeOfADateOutOfTheCalendarsRange_LeavesTheDate()
    {
        var value = new DateTime(1, 1, 1, 9, 30, 45);
        var field = Shown(new DateTimeField { Language = "ar-SA", Format = DateTimeFieldFormat.LongTime, Value = value });
        Press(field, Key.RightArrow);

        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(value.AddMinutes(1)));
    }

    [Test]
    public void ANumberBelowWhereTheEraBegins_IsItsFirst()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1989, 1, 20),
        });
        Press(field, Key.Tab);
        Press(field, Key.Tab);
        Press(field, Key.Tab);

        Type(field, "5");
        Press(field, Key.Tab, InputModifiers.LeftShift);

        Assert.That(field.Value, Is.EqualTo(new DateTime(1989, 1, 8)), "Heisei began on the 8th");
    }

    [Test]
    public void AYInQuotes_IsNoYear()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Format = DateTimeFieldFormat.Custom,
            CustomFormat = "HH:mm 'every day'", Value = new DateTime(2026, 10, 19, 9, 0, 0),
        }, focused: false);

        Assert.That(Kinds(field), Is.EqualTo(new[] { DateTimeSegmentKind.Hour, DateTimeSegmentKind.Minute }));
        Assert.That(Literals(field), Does.Contain("every day"));
    }

    [Test]
    public void TheMonth_ComesBackWhenTheEraHasIt()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(2049, 12, 31),
        });

        Press(field, Key.DownArrow);
        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.EqualTo(new DateTime(2049, 12, 31)), "Reiwa 31, December, again");
    }

    [Test]
    public void APatternWithoutTheEra_ShowsItForACalendarOfEras()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Format = DateTimeFieldFormat.Custom,
            CustomFormat = "yyyy/MM/dd", Value = new DateTime(2018, 6, 1),
        }, focused: false);

        Assert.That(Kinds(field)[0], Is.EqualTo(DateTimeSegmentKind.Era));
    }

    [Test]
    public void AnEraYear_AcceptedAndLeft_StaysWhenTheEraChangesBack()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1975, 6, 1),
        });

        Press(field, Key.DownArrow);
        Press(field, Key.RightArrow);
        Press(field, Key.RightArrow);
        Press(field, Key.UpArrow);
        Press(field, Key.LeftArrow);
        Press(field, Key.LeftArrow);
        Press(field, Key.UpArrow);

        Assert.That(new System.Globalization.JapaneseCalendar().GetYear(field.Value.Value), Is.EqualTo(15),
            "Taisho 15 was kept when the month was changed in it");
    }

    [Test]
    public void AnEraYear_ComesBackWhenTheEraHasIt()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1975, 6, 1),
        });

        Press(field, Key.DownArrow);
        Press(field, Key.UpArrow);

        Assert.That(field.Value?.Year, Is.EqualTo(1975), "Showa 50 again, not Showa 15");
    }

    [TestCase("MMMM yyyy", 2026, 1, 31)]
    [TestCase("yyyy", 2024, 2, 29)]
    public void AFormatWithoutTheDay_StillMakesADate(string pattern, int year, int month, int day)
    {
        var field = Shown(new DateTimeField
        {
            Language = "en-US", Format = DateTimeFieldFormat.Custom, CustomFormat = pattern,
            Value = new DateTime(year, month, day),
        });

        Press(field, Key.UpArrow);

        Assert.That(field.Value, Is.Not.Null);
    }

    [Test]
    public void ASourceThatAnswersNull_EmptiesTheParts()
    {
        var source = new CappedDate { Date = new DateTime(2026, 10, 19), RefusesPastCap = true };
        var field = new DateTimeField { Language = "ru-RU", DataContext = source };
        field.SetBinding(DateTimeField.ValueProperty, new Adamantium.UI.Core.Data.Binding(nameof(CappedDate.Date)));
        Shown(field);
        Press(field, Key.RightArrow);
        Press(field, Key.RightArrow);

        Type(field, "2030");
        Settle();

        Assert.That(source.Date, Is.Null);
        Assert.That(field.Segments.Last().IsEmpty);
    }

    [Test]
    public void AValueClearedFromOutside_ForgetsItsDate()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ru-RU", Format = DateTimeFieldFormat.ShortTime, Value = new DateTime(2020, 1, 1, 9, 30, 0),
        });

        field.Value = null;
        Type(field, "1030");

        Assert.That(field.Value, Is.EqualTo(DateTime.Today.AddHours(10).AddMinutes(30)));
    }

    [Test]
    public void AValueTypedFromNothing_HasNoKind()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });

        Type(field, "19102026");

        Assert.That(field.Value?.Kind, Is.EqualTo(DateTimeKind.Unspecified));
    }

    [Test]
    public void AShortEra_MovesOnAfterADigitNoSecondCanFollow()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, Value = new DateTime(1920, 6, 1),
        });
        Press(field, Key.RightArrow);

        Type(field, "2");

        Assert.That(field.ActiveSegment.Kind, Is.Not.EqualTo(DateTimeSegmentKind.Year), "Taisho has no twentieth year");
    }

    [TestCase(2025, 4, 20)]
    [TestCase(2024, 3, 20)]
    public void HebrewMonths_AreNamedForTheirYear(int year, int month, int day)
    {
        var date = new DateTime(year, month, day);
        var field = Shown(new DateTimeField
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, Format = DateTimeFieldFormat.Custom,
            CustomFormat = "MMMM", Value = date,
        }, focused: false);
        var hebrew = (System.Globalization.CultureInfo)new System.Globalization.CultureInfo("he-IL").Clone();
        hebrew.DateTimeFormat.Calendar = new System.Globalization.HebrewCalendar();

        Assert.That(field.Segments[0].Text, Is.EqualTo(date.ToString("MMMM", hebrew)));
    }

    [TestCase(5784, 8, 12, 5785, 7)]
    [TestCase(5784, 7, 10, 5785, 6)]
    [TestCase(5785, 6, 10, 5784, 7)]
    public void AHebrewYearStep_KeepsTheMonthAcrossALeapYear(int year, int month, int day, int toYear, int toMonth)
    {
        var hebrew = new System.Globalization.HebrewCalendar();
        var field = Shown(new DateTimeField
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, Format = DateTimeFieldFormat.Custom,
            CustomFormat = "yyyy/MM/dd", Value = hebrew.ToDateTime(year, month, day, 0, 0, 0, 0),
        });

        Press(field, toYear > year ? Key.UpArrow : Key.DownArrow);

        Assert.That(field.Value, Is.EqualTo(hebrew.ToDateTime(toYear, toMonth, day, 0, 0, 0, 0)),
            "Nisan stays Nisan, Adar II and Adar answer each other");
    }

    [Test]
    public void ChangingTheFormatWhileFocused_KeepsTheKeyboardOnAnEditablePart()
    {
        var field = Shown(new DateTimeField { Language = "en-US", Value = new DateTime(2026, 10, 19) });

        field.Format = DateTimeFieldFormat.LongDate;
        Settle();

        Assert.That(field.ActiveSegment.Kind, Is.EqualTo(DateTimeSegmentKind.Month), "it stays on the month");
    }

    [Test]
    public void PartsTyped_SurviveALanguageChange()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU" });
        Type(field, "1910");

        field.Language = "en-US";
        Settle();

        Assert.That(field.Value, Is.Null);
        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Day).Text, Is.EqualTo("19"));
        Assert.That(field.Segments.First(s => s.Kind == DateTimeSegmentKind.Month).Text, Is.EqualTo("10"));
    }

    [Test]
    public void FocusedBeforeItsTemplate_ItStillTakesTheKeyboard()
    {
        var field = new DateTimeField { Language = "ru-RU" };
        _window = new Window { Width = 800, Height = 600, Content = field };
        FocusManager.Focus(field, NavigationMethod.Tab);
        _window.ApplyCurrentTheme();
        Settle();

        Type(field, "19");

        Assert.That(field.Segments[0].Text, Is.EqualTo("19"));
    }

    [Test]
    public void AStandardPatternLetter_IsThatPattern()
    {
        var field = Shown(new DateTimeField
        {
            Language = "ru-RU", Format = DateTimeFieldFormat.Custom, CustomFormat = "d", Value = new DateTime(2026, 10, 19),
        }, focused: false);

        Assert.That(Texts(field), Is.EqualTo(new[] { "19", "10", "2026" }));
    }

    [Test]
    public void Automation_SeesAnEditOfSpinners()
    {
        var field = Shown(new DateTimeField { Language = "ru-RU", Value = new DateTime(2026, 2, 10) }, focused: false);
        var peer = (DateTimeFieldAutomationPeer)field.GetAutomationPeer();

        Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Edit));
        Assert.That(peer.Value, Is.EqualTo("2026-02-10T00:00:00"));

        var day = (FieldSegmentAutomationPeer)peer.GetChildren()[0];
        Assert.That(day.ControlType, Is.EqualTo(AutomationControlType.Spinner));
        Assert.That(day.Maximum, Is.EqualTo(28), "February 2026");
        day.SetValue(14);
        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 2, 14)));

        peer.SetValue("2026-10-19T00:00:00");
        Assert.That(field.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
    }

    private sealed class CappedDate : System.ComponentModel.INotifyPropertyChanged
    {
        private static readonly DateTime Cap = new(2026, 12, 31);
        private DateTime? _date;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public bool RefusesPastCap { get; init; }

        public DateTime? Date
        {
            get => _date;
            set
            {
                _date = value > Cap ? RefusesPastCap ? null : Cap : value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Date)));
            }
        }
    }
}
