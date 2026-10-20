using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Dates, times and lengths of time in a table and in an inspector: the text a cell draws, the picker or the
/// length field it edits in, and the value going back as the type the record holds.</summary>
[TestFixture]
public class DateTimeGridEditorTests
{
    private FakeApp _app;
    private Window _window;

    public sealed class Record
    {
        public DateTime Due { get; set; } = new(2026, 10, 19, 14, 5, 0);

        public DateOnly Day { get; set; } = new(2026, 10, 19);

        public TimeOnly At { get; set; } = new(9, 30);

        public DateTimeOffset Stamp { get; set; } = new(2026, 10, 19, 14, 5, 0, TimeSpan.FromHours(3));

        public TimeSpan Length { get; set; } = new(1, 6, 0, 0);

        public DateTime? Maybe { get; set; }

        public DateOnly? Birthday { get; set; }

        public TimeSpan? Pause { get; set; } = TimeSpan.FromMinutes(5);
    }

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

    private T Shown<T>(T content) where T : UIComponent
    {
        _window = new Window { Width = 900, Height = 600, ClientWidth = 900, ClientHeight = 600, Content = content };
        _window.ApplyCurrentTheme();
        Settle();
        return content;
    }

    private void Settle()
    {
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(_window);
            BindingUpdateQueue.Flush();
            ((IMeasurableComponent)_window.Content).Measure(new Size(900, 600));
            ((IMeasurableComponent)_window.Content).Arrange(new Rect(0, 0, 900, 600));
        }
    }

    private static IEnumerable<T> Within<T>(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is T found) yield return found;
            foreach (var deep in Within<T>(child)) yield return deep;
        }
    }

    private static string TextIn(IUIComponent cell) => string.Concat(
        Within<TextBlock>(cell).Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text));

    private TreeDataGrid Table(Record record, params DataGridColumn[] columns)
    {
        var grid = new TreeDataGrid();
        foreach (var column in columns)
        {
            column.Width = new GridLength(180);
            grid.Columns.Add(column);
        }

        grid.ItemsSource = new List<Record> { record };
        return Shown(grid);
    }

    private T Editing<T>(TreeDataGrid grid, int column)
    {
        Assert.That(grid.BeginEdit(0, column), Is.True);
        Settle();
        return Within<T>(grid.CellFor(0, column)).First();
    }

    [Test]
    public void ADateColumn_DrawsItsFormat_InTheApplicationsLanguage()
    {
        var record = new Record();
        var grid = Table(record,
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.ShortDate },
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.At)), Format = DateTimeFieldFormat.ShortTime });
        var culture = Languages.Culture;

        Assert.Multiple(() =>
        {
            Assert.That(TextIn(grid.CellFor(0, 0)), Is.EqualTo(record.Due.ToString(culture.DateTimeFormat.ShortDatePattern, culture)));
            Assert.That(TextIn(grid.CellFor(0, 1)),
                Is.EqualTo(new DateTime(1, 1, 1, 9, 30, 0).ToString(culture.DateTimeFormat.ShortTimePattern, culture)));
        });
    }

    [Test]
    public void ANewFormat_RedrawsTheCells()
    {
        var record = new Record();
        var column = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)) };
        var grid = Table(record, column);

        column.Format = DateTimeFieldFormat.Custom;
        column.CustomFormat = "yyyy";
        Settle();

        Assert.That(TextIn(grid.CellFor(0, 0)), Is.EqualTo("2026"));
    }

    [TestCase(DateTimeFieldFormat.ShortDate, typeof(DatePicker))]
    [TestCase(DateTimeFieldFormat.LongTime, typeof(TimePicker))]
    [TestCase(DateTimeFieldFormat.ShortDateTime, typeof(DateTimePicker))]
    public void ACell_EditsInThePickerForItsParts(DateTimeFieldFormat format, Type picker)
    {
        var grid = Table(new Record(), new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = format });

        var editor = Editing<DateTimePickerBase>(grid, 0);

        Assert.Multiple(() =>
        {
            Assert.That(editor, Is.InstanceOf(picker));
            Assert.That(editor.Format, Is.EqualTo(format));
            Assert.That(editor.Value, Is.EqualTo(new DateTime(2026, 10, 19, 14, 5, 0)), "the editor opens on the cell's value");
            Assert.That(editor.Segments, Is.Not.Empty, "and wears the theme's template");
        });
    }

    [Test]
    public void APickedDate_GoesBackAsTheTypeTheRecordHolds()
    {
        var record = new Record();
        var grid = Table(record,
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) },
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Stamp)), Format = DateTimeFieldFormat.ShortDateTime },
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.At)), Format = DateTimeFieldFormat.ShortTime });

        Editing<DateTimeField>(grid, 0).Value = new DateTime(2026, 12, 31);
        Assert.That(grid.CommitEdit(), Is.True);
        Editing<DateTimeField>(grid, 1).Value = new DateTime(2027, 1, 2, 8, 0, 0);
        Assert.That(grid.CommitEdit(), Is.True);
        Editing<DateTimeField>(grid, 2).Value = new DateTime(2026, 10, 19, 18, 45, 0);
        Assert.That(grid.CommitEdit(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(record.Day, Is.EqualTo(new DateOnly(2026, 12, 31)));
            Assert.That(record.Stamp, Is.EqualTo(new DateTimeOffset(2027, 1, 2, 8, 0, 0, TimeSpan.FromHours(3))), "its offset kept");
            Assert.That(record.At, Is.EqualTo(new TimeOnly(18, 45)));
        });
    }

    [Test]
    public void ADurationColumn_DrawsItsParts_AndEditsInALengthField()
    {
        var record = new Record();
        var grid = Table(record,
            new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) },
            new DataGridDurationColumn
            {
                Binding = new Binding(nameof(Record.Length)),
                Parts = DurationParts.Days | DurationParts.Hours | DurationParts.Minutes,
            });

        Assert.Multiple(() =>
        {
            Assert.That(TextIn(grid.CellFor(0, 0)), Is.EqualTo("30:00:00"), "the hours count the day");
            Assert.That(TextIn(grid.CellFor(0, 1)), Is.EqualTo("1d06:00").Or.EqualTo("1д06:00"));
        });

        var field = Editing<DurationField>(grid, 1);
        Assert.Multiple(() =>
        {
            Assert.That(field.Parts, Is.EqualTo(DurationParts.Days | DurationParts.Hours | DurationParts.Minutes));
            Assert.That(field.Value, Is.EqualTo(record.Length));
        });

        field.Value = TimeSpan.FromMinutes(90);
        Assert.That(grid.CommitEdit(), Is.True);
        Assert.That(record.Length, Is.EqualTo(TimeSpan.FromMinutes(90)));
    }

    [Test]
    public void AnInspectorBuiltFromAType_PicksTheEditorByTheType()
    {
        var lines = new PropertyDefinitionBuilder().Build(typeof(Record)).ToDictionary(d => d.Binding is Binding b ? b.Path.Path : "");

        Assert.Multiple(() =>
        {
            Assert.That(((DateTimeProperty)lines[nameof(Record.Due)]).Format, Is.EqualTo(DateTimeFieldFormat.ShortDateTime));
            Assert.That(((DateTimeProperty)lines[nameof(Record.Day)]).Format, Is.EqualTo(DateTimeFieldFormat.ShortDate));
            Assert.That(((DateTimeProperty)lines[nameof(Record.At)]).Format, Is.EqualTo(DateTimeFieldFormat.ShortTime));
            Assert.That(((DateTimeProperty)lines[nameof(Record.Stamp)]).Format, Is.EqualTo(DateTimeFieldFormat.ShortDateTime));
            Assert.That(((DateTimeProperty)lines[nameof(Record.Maybe)]).Format, Is.EqualTo(DateTimeFieldFormat.ShortDateTime));
            Assert.That(lines[nameof(Record.Length)], Is.InstanceOf<DurationProperty>());
        });
    }

    private static IEnumerable<PropertyRow> Rows(IUIComponent within) => Within<PropertyRow>(within);

    [Test]
    public void AnInspectorLine_WritesWhatIsPicked_AsThePropertysType()
    {
        var record = new Record();
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = record });

        T EditorOf<T>(string header) => Within<T>(Rows(grid).First(r => Equals(r.Definition?.Header, header))).First();

        var day = EditorOf<DateTimePickerBase>("Day");
        var length = EditorOf<DurationField>("Length");
        Assert.Multiple(() =>
        {
            Assert.That(day, Is.InstanceOf<DatePicker>());
            Assert.That(day.Value, Is.EqualTo(new DateTime(2026, 10, 19)));
            Assert.That(length.Value, Is.EqualTo(record.Length));
        });

        day.Value = new DateTime(2027, 3, 8);
        length.Value = TimeSpan.FromSeconds(45);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(record.Day, Is.EqualTo(new DateOnly(2027, 3, 8)));
            Assert.That(record.Length, Is.EqualTo(TimeSpan.FromSeconds(45)));
        });
    }

    private static void Press(IInputComponent target, Key key)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(args);
    }

    private static void Type(IInputComponent target, string text) =>
        target.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    [Test]
    public void ACharacterTypedOnACell_OpensItsFieldAndGoesIn_AndEnterKeepsWhatWasTyped()
    {
        var record = new Record();
        var grid = Table(record, new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) });
        grid.SelectCell(0, 0);

        Type(grid, "5");
        Settle();
        var field = Within<DurationField>(grid.CellFor(0, 0)).First();
        Assert.That(field.ActiveSegment.Text, Is.EqualTo("5"), "the character went into the hours being typed");

        Press(field, Key.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(record.Length, Is.EqualTo(TimeSpan.FromHours(5)), "the hours typed and not yet moved on from count");
        });
    }

    [Test]
    public void AnEditCommittedFromTheGrid_KeepsTheDigitsBeingTyped()
    {
        var record = new Record();
        var grid = Table(record, new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) });
        grid.SelectCell(0, 0);

        Type(grid, "5");
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(grid.CommitEdit(), Is.True);
            Assert.That(record.Length, Is.EqualTo(TimeSpan.FromHours(5)), "not only Enter counts what was typed");
        });
    }

    [Test]
    public void ACharacterCommittedBeforeItsFieldArrives_IsReadAsTheColumnReadsText()
    {
        var row = new Loose();
        var grid = new TreeDataGrid();
        var lengths = new DataGridDurationColumn { Binding = new Binding(nameof(Loose.Length)), Width = new GridLength(180) };
        grid.Columns.Add(lengths);
        grid.ItemsSource = new List<Loose> { row };
        Shown(grid);
        grid.SelectCell(0, 0);

        Type(grid, "x");
        grid.CommitEdit();

        Assert.That(row.Length, Is.EqualTo(TimeSpan.FromHours(1)), "a letter is no length - not written as text");

        Type(grid, "5");
        grid.CommitEdit();
        Assert.That(row.Length, Is.EqualTo(TimeSpan.FromHours(1)), "nor is a digit guessed at before the field takes it");

        Type(grid, "7");
        grid.CancelEdit();
        Settle();
        Assert.That(grid.BeginEdit(0, 0), Is.True);
        Settle();
        Assert.That(grid.CommitEdit(), Is.True);
        Assert.That(row.Length, Is.EqualTo(TimeSpan.FromHours(1)), "a key cancelled is not typed into the next edit");
    }

    [Test]
    public void ANullDayInTheTable_TakesAPickedOne_AndAnEmptiedNullableIsWrittenEmpty()
    {
        var record = new Record();
        var grid = Table(record,
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Birthday)) },
            new DataGridDurationColumn { Binding = new Binding(nameof(Record.Pause)) },
            new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) });

        Editing<DateTimeField>(grid, 0).Value = new DateTime(1990, 5, 17);
        Assert.That(grid.CommitEdit(), Is.True);
        Editing<DurationField>(grid, 1).Value = null;
        Assert.That(grid.CommitEdit(), Is.True);
        Editing<DurationField>(grid, 2).Value = null;

        Assert.Multiple(() =>
        {
            Assert.That(record.Birthday, Is.EqualTo(new DateOnly(1990, 5, 17)), "a DateOnly? that held nothing");
            Assert.That(record.Pause, Is.Null, "a length that can be empty is emptied");
            Assert.That(grid.CommitEdit(), Is.True, "one that cannot keeps what it held rather than holding the edit open");
            Assert.That(record.Length, Is.EqualTo(new TimeSpan(1, 6, 0, 0)));
        });
    }

    [Test]
    public void SearchFilterAndCopy_ReadTheCellAsItIsDrawn()
    {
        var record = new Record();
        var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.ShortDate };
        var lengths = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) };
        var grid = Table(record, dates, lengths);
        var culture = Languages.Culture;
        var shownDate = record.Due.ToString(culture.DateTimeFormat.ShortDatePattern, culture);

        Assert.Multiple(() =>
        {
            Assert.That(dates.TextOf(record.Due), Is.EqualTo(shownDate));
            Assert.That(lengths.TextOf(record.Length), Is.EqualTo("30:00:00"));
            Assert.That(lengths.ValueOfText("30:00:00", null), Is.EqualTo(record.Length), "what is copied pastes as itself");
            Assert.That(dates.ValueOfText(shownDate, null), Is.EqualTo(record.Due.Date));
            Assert.That(dates.ValueOfText(shownDate, record.Due), Is.EqualTo(record.Due),
                "the time a short date does not show is kept from the value pasted over");
            Assert.That(((Adamantium.UI.Controls.Automation.DataGridCellAutomationPeer)grid.CellFor(0, 1).GetAutomationPeer()).Value,
                Is.EqualTo("30:00:00"), "automation reads the whole length");
        });

        grid.SelectCell(0, 0);
        grid.SelectCell(0, 1, extend: true);
        Assert.That(grid.GetSelectionAsText(), Is.EqualTo(shownDate + "\t30:00:00"));
    }

    [Test]
    public void AnInspectorFillsANullDayOnly_AndEachObjectKeepsItsOwnOffset()
    {
        var first = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(3)) };
        var second = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero) };
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = first });
        var birthday = Within<DateTimePickerBase>(Rows(grid).First(r => Equals(r.Definition?.Header, "Birthday"))).First();

        birthday.Value = new DateTime(1990, 5, 17);
        Settle();
        Assert.That(first.Birthday, Is.EqualTo(new DateOnly(1990, 5, 17)));

        grid.SelectedObject = null;
        grid.SelectedObjects = new object[] { first, second };
        Settle();
        var stamp = Within<DateTimePickerBase>(Rows(grid).First(r => Equals(r.Definition?.Header, "Stamp"))).First();
        Assert.That(stamp.Value, Is.Null, "the same instant at two offsets is two values");

        stamp.Value = new DateTime(2026, 2, 2, 12, 0, 0);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(first.Stamp, Is.EqualTo(new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.FromHours(3))));
            Assert.That(second.Stamp, Is.EqualTo(new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void AColumnOfNoFormat_ReadsTheFormatOffWhatItHolds()
    {
        var record = new Record();
        var grid = Table(record, new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.At)) });
        var culture = Languages.Culture;

        Assert.That(TextIn(grid.CellFor(0, 0)),
            Is.EqualTo(new DateTime(1, 1, 1, 9, 30, 0).ToString(culture.DateTimeFormat.ShortTimePattern, culture)));
        Assert.That(Editing<DateTimePickerBase>(grid, 0), Is.InstanceOf<TimePicker>());
    }

    [Test]
    public void AFilterGreaterThan_ComparesLengthsAndDays_NotText()
    {
        var lengths = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) };
        var days = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) };
        var filter = new DataGridColumnFilter();
        filter.First.Operator = DataGridFilterOperator.IsGreaterThan;
        filter.First.Value = "25:00:00";
        var length = new TimeSpan(1, 6, 0, 0);
        var shownDay = new DateTime(2026, 10, 1).ToString(Languages.Culture.DateTimeFormat.ShortDatePattern, Languages.Culture);
        var dayFilter = new DataGridColumnFilter();
        dayFilter.First.Operator = DataGridFilterOperator.IsLessThan;
        dayFilter.First.Value = shownDay;

        Assert.Multiple(() =>
        {
            Assert.That(filter.Passes(length, lengths.TextOf(length), t => lengths.ValueOfText(t, null)), Is.True, "30 hours is past 25");
            Assert.That(filter.Passes(TimeSpan.FromHours(2), "02:00:00", t => lengths.ValueOfText(t, null)), Is.False);
            Assert.That(dayFilter.Passes(new DateOnly(2026, 9, 30), "", t => days.ValueOfText(t, null)), Is.True);
            Assert.That(dayFilter.Passes(new DateOnly(2026, 10, 19), "", t => days.ValueOfText(t, null)), Is.False);
        });
    }

    [Test]
    public void AutomationWritesBackWhatItReads()
    {
        var record = new Record();
        var grid = Table(record, new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) },
            new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) });
        var length = (Adamantium.UI.Controls.Automation.DataGridCellAutomationPeer)grid.CellFor(0, 0).GetAutomationPeer();
        var day = (Adamantium.UI.Controls.Automation.DataGridCellAutomationPeer)grid.CellFor(0, 1).GetAutomationPeer();
        var shown = new DateTime(2027, 1, 2).ToString(Languages.Culture.DateTimeFormat.ShortDatePattern, Languages.Culture);

        length.SetValue("02:15:00");
        day.SetValue(shown);

        Assert.Multiple(() =>
        {
            Assert.That(record.Length, Is.EqualTo(new TimeSpan(2, 15, 0)));
            Assert.That(record.Day, Is.EqualTo(new DateOnly(2027, 1, 2)));
        });
    }

    [Test]
    public void APickerClosedFromOutsideTheTable_EndsTheEdit_AndOneClosedFromWithinDoesNot()
    {
        var record = new Record();
        var grid = Table(record, new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) });
        var picker = Editing<DatePicker>(grid, 0);

        picker.IsDropDownOpen = true;
        Settle();
        picker.IsDropDownOpen = false;
        Assert.That(grid.IsEditing, Is.True, "closed while the picker still had the keyboard");

        picker.IsDropDownOpen = true;
        Settle();
        picker.Value = new DateTime(2026, 12, 1);
        FocusManager.Focus(grid, NavigationMethod.Unspecified);
        picker.IsDropDownOpen = false;

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False, "a click elsewhere closed it");
            Assert.That(record.Day, Is.EqualTo(new DateOnly(2026, 12, 1)), "and what was picked was kept");
        });
    }

    public sealed class Counted
    {
        public int? Count { get; set; } = 5;

        public int Size { get; set; } = 7;
    }

    [Test]
    public void AnInspectorEmptiesANullableNumber_AndShowsAgainANumberThatCannotBeEmpty()
    {
        var counted = new Counted();
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = counted });
        var count = Rows(grid).First(r => Equals(r.Definition?.Header, "Count"));
        var size = Rows(grid).First(r => Equals(r.Definition?.Header, "Size"));

        Assert.Multiple(() =>
        {
            Assert.That(grid.Write(count, null), Is.True);
            Assert.That(counted.Count, Is.Null);
            Assert.That(grid.Write(size, null), Is.False);
            Assert.That(counted.Size, Is.EqualTo(7), "not its zero");
            Assert.That(Within<NumericUpDown>(size).First().Value, Is.EqualTo(7), "and the field shows the 7 again");
        });
    }

    [Test]
    public void AMarkupDefault_ReadsAsTheLinesType()
    {
        var record = new Record { Length = TimeSpan.FromMinutes(30) };
        var section = new PropertySection { Header = "Times", Target = record, IsExpanded = true };
        section.Properties.Add(new DurationProperty
        {
            Header = "Length", Binding = new Binding(nameof(Record.Length)), DefaultValue = "00:30:00",
        });
        var grid = new PropertyGrid();
        grid.Sections.Add(section);
        Shown(grid);
        var row = Rows(grid).First();

        Assert.That(row.IsModified, Is.False, "thirty minutes is the default written as text");

        grid.Write(row, TimeSpan.FromHours(1));
        Assert.That(row.ResetToDefault(), Is.True);
        Assert.That(record.Length, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void ALengthReadsWithTheThemesWordForDays()
    {
        var record = new Record();
        var lengths = new DataGridDurationColumn
        {
            Binding = new Binding(nameof(Record.Length)), Parts = DurationParts.Days | DurationParts.Hours | DurationParts.Minutes,
        };
        var grid = Table(record, lengths);

        Assert.Multiple(() =>
        {
            Assert.That(lengths.TextOf(record.Length), Is.EqualTo("1 d 06:00").Or.EqualTo("1 д 06:00"));
            Assert.That(lengths.ValueOfText(lengths.TextOf(record.Length), null), Is.EqualTo(record.Length));
            Assert.That(TextIn(grid.CellFor(0, 0)), Is.EqualTo("1d06:00").Or.EqualTo("1д06:00"));
        });
    }

    [Test]
    public void ANewLanguage_KeepsWhatTheFilterTicked_AndRedrawsTheDates()
    {
        var was = Languages.Current;
        try
        {
            var record = new Record();
            var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) };
            var grid = Table(record, dates);
            var filter = grid.FilterFor(dates);
            filter.Included = [dates.TextOf(record.Day)];
            grid.ApplyFilters();
            Settle();

            Languages.Current = "ru-RU";
            Settle();
            var russian = new DateTime(2026, 10, 19).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

            Assert.Multiple(() =>
            {
                Assert.That(filter.Included, Is.EquivalentTo(new[] { russian }), "the tick follows the value into the new language");
                Assert.That(grid.CellFor(0, 0), Is.Not.Null, "and the row it let through is still there");
                Assert.That(TextIn(grid.CellFor(0, 0)), Is.EqualTo(russian));
            });
        }
        finally
        {
            Languages.Current = was;
        }
    }

    [Test]
    public void ObjectsThatDisagree_SayItInPlaceOfTheParts()
    {
        var first = new Record { Day = new DateOnly(2026, 1, 1), Length = TimeSpan.FromHours(1) };
        var second = new Record { Day = new DateOnly(2026, 2, 2), Length = TimeSpan.FromHours(2) };
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObjects = new object[] { first, second } });
        var day = Within<DateTimePickerBase>(Rows(grid).First(r => Equals(r.Definition?.Header, "Day"))).First();
        var length = Within<DurationField>(Rows(grid).First(r => Equals(r.Definition?.Header, "Length"))).First();

        Assert.Multiple(() =>
        {
            Assert.That(day.Placeholder, Is.EqualTo(grid.MixedText).And.Not.Empty);
            Assert.That(length.Placeholder, Is.EqualTo(grid.MixedText));
            Assert.That(day.Segments.All(s => s.Visibility == Visibility.Collapsed), "the parts give way to the words");
            Assert.That(length.Segments.All(s => s.Visibility == Visibility.Collapsed));
            Assert.That(Within<TextBlock>(length).Single(t => t.Classes.Contains("DateTimeFieldPrompt")).Text,
                Is.EqualTo(grid.MixedText));
        });
    }

    [Test]
    public void AnEmptiedPart_LeavesAPropertyThatCannotBeEmpty_AsItWas()
    {
        var record = new Record();
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = record });
        var day = Within<DateTimePickerBase>(Rows(grid).First(r => Equals(r.Definition?.Header, "Day"))).First();
        var maybe = Within<DateTimePickerBase>(Rows(grid).First(r => Equals(r.Definition?.Header, "Maybe"))).First();
        maybe.Value = new DateTime(2026, 1, 1, 10, 0, 0);
        Settle();

        day.Value = null;
        maybe.Value = null;
        Settle();
        Assert.That(record.Maybe, Is.Not.Null, "an emptied part is not yet an emptied date");

        Press(day, Key.Enter);
        Press(maybe, Key.Enter);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(record.Day, Is.EqualTo(new DateOnly(2026, 10, 19)));
            Assert.That(day.Value, Is.EqualTo(new DateTime(2026, 10, 19)), "and the field shows the day again");
            Assert.That(record.Maybe, Is.Null, "Enter empties one that can be empty");
        });
    }

    [Test]
    public void TextNoPatternCanWrite_OrNoLengthCanHold_IsNoCrash()
    {
        var odd = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.Custom, CustomFormat = "Q" };
        var lengths = new DataGridDurationColumn
        {
            Binding = new Binding(nameof(Record.Length)), Parts = DurationParts.Days | DurationParts.Hours | DurationParts.Minutes,
        };

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => odd.TextOf(new DateTime(2026, 10, 19)));
            Assert.That(lengths.ValueOfText("99999999999999999 00:00", null), Is.SameAs(AdamantiumProperty.UnsetValue),
                "too long is no length - refused");
            Assert.That(lengths.ValueOfText("02:30:00", null), Is.EqualTo(new TimeSpan(2, 30, 0)), "text in the .NET form, not the column's own, reads as the .NET form");
        });
    }

    public sealed class Flags
    {
        public bool? Maybe { get; set; } = true;

        public DateOnly Day { get; set; } = new(2026, 2, 10);
    }

    [Test]
    public void InspectorAutomation_TogglesANullableFlag_AndWritesBackTheDayItReads()
    {
        var flags = new Flags();
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = flags });
        var maybe = Rows(grid).First(r => Equals(r.Definition?.Header, "Maybe"));
        var day = Rows(grid).First(r => Equals(r.Definition?.Header, "Day"));
        var dayPeer = (Adamantium.UI.Controls.Automation.PropertyRowAutomationPeer)day.GetAutomationPeer();

        Assert.That(maybe.GetAutomationPeer().GetPattern(Adamantium.UI.Core.Automation.PatternId.Toggle), Is.Not.Null);

        var read = dayPeer.Value;
        Assert.That(grid.Write(day, new DateTime(2030, 1, 1)), Is.True);
        Assert.That(flags.Day, Is.EqualTo(new DateOnly(2030, 1, 1)));
        dayPeer.SetValue(read);

        Assert.That(flags.Day, Is.EqualTo(new DateOnly(2026, 2, 10)), "February the 10th, not October the 2nd");
    }

    [TestCase(DurationParts.Minutes | DurationParts.Seconds, 0, 5, 30, 0)]
    [TestCase(DurationParts.Seconds, 0, 0, 30, 0)]
    [TestCase(DurationParts.Hours | DurationParts.Seconds, 1, 0, 30, 0)]
    [TestCase(DurationParts.Hours | DurationParts.Minutes | DurationParts.Milliseconds, 1, 30, 0, 45)]
    [TestCase(DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds | DurationParts.Milliseconds, 26, 3, 4, 500)]
    public void ALengthsOwnText_ReadsBackAsTheSameLength(DurationParts parts, int hours, int minutes, int seconds, int milliseconds)
    {
        var column = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)), Parts = parts };
        var length = new TimeSpan(0, hours, minutes, seconds, milliseconds);

        Assert.That(column.ValueOfText(column.TextOf(length), null), Is.EqualTo(length), column.TextOf(length));
    }

    [TestCase(DurationParts.Days | DurationParts.Hours | DurationParts.Minutes, "02:15:00", 0, 2, 15, 0, 0)]
    [TestCase(DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds | DurationParts.Milliseconds, "1.02:03:04", 1, 2, 3, 4, 0)]
    [TestCase(DurationParts.Days | DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds, "02:03:04.05", 0, 2, 3, 4, 50)]
    [TestCase(DurationParts.Seconds | DurationParts.Milliseconds, "00:00:04.5", 0, 0, 0, 4, 500)]
    public void TheDotNetForm_IsNotTakenForTheColumnsOwn(DurationParts parts, string text, int days, int hours, int minutes,
        int seconds, int milliseconds)
    {
        var column = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)), Parts = parts };

        Assert.That(column.ValueOfText(text, null), Is.EqualTo(new TimeSpan(days, hours, minutes, seconds, milliseconds)));
    }

    [Test]
    public void AnInspectorLineWrittenItsOwnText_KeepsWhatTheTextDoesNotShow()
    {
        var line = new DateTimeProperty { Format = DateTimeFieldFormat.ShortDate };
        var length = new DurationProperty();
        var held = new DateTime(2026, 10, 20, 10, 30, 45);
        var shown = line.AutomationTextOf(held);

        Assert.Multiple(() =>
        {
            Assert.That(line.TryConvert(shown, typeof(DateTime), held, out var date), Is.True);
            Assert.That(date, Is.EqualTo(held), "the time a short date does not show");
            Assert.That(length.TryConvert(length.AutomationTextOf(TimeSpan.FromMilliseconds(1500)), typeof(TimeSpan),
                TimeSpan.FromMilliseconds(1500), out var kept), Is.True);
            Assert.That(kept, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
            Assert.That(line.TryConvert("2026-10-20T10:30:00Z", typeof(DateTimeOffset), null, out var moment), Is.True);
            Assert.That(moment, Is.EqualTo(new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero)), "a moment");
            var atMoscow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3));
            Assert.That(((DateTimeOffset)DateTimeValues.FromMoment((DateTimeOffset)moment, typeof(DateTimeOffset), atMoscow)).EqualsExact(
                new DateTimeOffset(2026, 10, 20, 13, 30, 0, TimeSpan.FromHours(3))), "13:30 at the held +03:00");
            Assert.That(DateTimeValues.FromMoment((DateTimeOffset)moment, typeof(DateTime), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Is.EqualTo(new DateTime(2026, 10, 20, 10, 30, 0, DateTimeKind.Utc)), "10:30 for a UTC one, not this machine's time");
        });
    }

    [Test]
    public void TheLinesOwnFormComesFirst_AndATimeAloneIsToday()
    {
        var was = Languages.Current;
        try
        {
            Languages.Current = "ru-RU";
            var line = new DateTimeProperty { Format = DateTimeFieldFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm zzz" };
            var time = new DateTimeProperty { Format = DateTimeFieldFormat.ShortTime };

            Assert.Multiple(() =>
            {
                Assert.That(line.TryConvert("05.10.2026 14:30 +03:00", typeof(DateTimeOffset), null, out var own), Is.True);
                Assert.That(own, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 14, 30, 0, TimeSpan.FromHours(3))), "the fifth of October");
                Assert.That(time.TryConvert("14:30", typeof(DateTime), null, out var alone), Is.True);
                Assert.That(alone, Is.EqualTo(DateTime.Today.AddHours(14.5)), "not the year one");
            });
        }
        finally
        {
            Languages.Current = was;
        }
    }

    [Test]
    public void ADayWordWithAPoint_StillReadsBack()
    {
        var column = new DataGridDurationColumn
        {
            Binding = new Binding(nameof(Record.Length)), Parts = DurationParts.Days | DurationParts.Hours | DurationParts.Minutes,
            DayUnit = "дн.",
        };
        var length = new TimeSpan(1, 2, 3, 0);

        Assert.That(column.ValueOfText(column.TextOf(length), null), Is.EqualTo(length), column.TextOf(length));
    }

    [Test]
    public void AResetWritesTheDefaultWhole()
    {
        var record = new Record { Length = new TimeSpan(0, 1, 0, 0, 250) };
        var section = new PropertySection { Header = "Times", Target = record, IsExpanded = true };
        section.Properties.Add(new DurationProperty
        {
            Header = "Length", Binding = new Binding(nameof(Record.Length)), DefaultValue = "01:30:00",
        });
        var grid = new PropertyGrid();
        grid.Sections.Add(section);
        Shown(grid);
        var row = Rows(grid).First();

        Assert.That(row.ResetToDefault(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(record.Length, Is.EqualTo(new TimeSpan(1, 30, 0)), "not 01:30:00.250");
            Assert.That(row.IsModified, Is.False);
        });
    }

    [Test]
    public void APasteOverALength_KeepsWhatItsColumnDoesNotShow()
    {
        var column = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)) };

        Assert.That(column.ValueOfText("02:00:00", new TimeSpan(0, 1, 0, 0, 750)), Is.EqualTo(new TimeSpan(0, 2, 0, 0, 750)));
    }

    [Test]
    public void AnIsoDefault_IsNeverReadDayForMonth()
    {
        var was = Languages.Current;
        try
        {
            Languages.Current = "ru-RU";
            var line = new DateTimeProperty();

            Assert.Multiple(() =>
            {
                Assert.That(line.TryConvert("2026-10-05", typeof(DateOnly), out var iso), Is.True);
                Assert.That(DateTimeValues.ToDateTime(iso), Is.EqualTo(new DateTime(2026, 10, 5)));
                Assert.That(line.TryConvert("05.10.2026", typeof(DateOnly), out var russian), Is.True);
                Assert.That(DateTimeValues.ToDateTime(russian)?.Date, Is.EqualTo(new DateTime(2026, 10, 5)), "the fifth of October");
            });
        }
        finally
        {
            Languages.Current = was;
        }
    }

    [Test]
    public void ANewLanguage_LeavesAContainsConditionAsTyped()
    {
        var was = Languages.Current;
        try
        {
            var record = new Record();
            var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)) };
            var grid = Table(record, dates);
            var filter = grid.FilterFor(dates);
            filter.First.Operator = DataGridFilterOperator.Contains;
            filter.First.Value = "14:05";

            Languages.Current = "ru-RU";
            Settle();

            Assert.That(filter.First.Value, Is.EqualTo("14:05"));
        }
        finally
        {
            Languages.Current = was;
        }
    }

    public sealed class Unset
    {
        public bool? Maybe { get; set; }
    }

    [Test]
    public void AnUnsetFlag_ReadsAsNeither()
    {
        var grid = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObject = new Unset() });
        var peer = (Adamantium.UI.Controls.Automation.PropertyRowAutomationPeer)Rows(grid).First().GetAutomationPeer();

        Assert.That(peer.ToggleState, Is.EqualTo(Adamantium.UI.Core.Automation.ToggleState.Indeterminate));
    }

    [Test]
    public void ANewLanguage_TranslatesAFiltersConditionsToo()
    {
        var was = Languages.Current;
        try
        {
            var record = new Record();
            var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Day)) };
            var grid = Table(record, dates);
            var filter = grid.FilterFor(dates);
            filter.First.Operator = DataGridFilterOperator.IsEqualTo;
            filter.First.Value = dates.TextOf(record.Day);
            grid.ApplyFilters();
            Settle();

            Languages.Current = "ru-RU";
            Settle();

            Assert.Multiple(() =>
            {
                Assert.That(filter.First.Value, Is.EqualTo("19.10.2026"));
                Assert.That(grid.CellFor(0, 0), Is.Not.Null, "the row it let through is still there");
            });
        }
        finally
        {
            Languages.Current = was;
        }
    }

    private static object Read(string text, string pattern, object current) =>
        DateTimeValues.TryParseMoment(text, DateTimeFieldFormat.Custom, pattern, System.Globalization.CultureInfo.InvariantCulture,
            current, out var value) ? value : null;

    [Test]
    public void AnIsoTimeWithoutAZone_IsTheClockAsTyped_NotAMomentHere()
    {
        var utc = new DateTime(2026, 1, 1, 0, 0, 45, DateTimeKind.Utc);
        var read = DateTimeValues.TryParseMoment("2026-10-05T14:30", DateTimeFieldFormat.ShortDateTime, null,
            System.Globalization.CultureInfo.InvariantCulture, utc, out var value) ? value : null;

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.InstanceOf<DateTime>());
            Assert.That(read, Is.EqualTo(new DateTime(2026, 10, 5, 14, 30, 45)), "the seconds it does not name are kept");
            Assert.That(DateTimeValues.FromDateTime((DateTime)read, typeof(DateTime), utc),
                Is.EqualTo(new DateTime(2026, 10, 5, 14, 30, 45, DateTimeKind.Utc)));
            Assert.That(Read("2026-10-05T14:30Z", "dd.MM.yyyy", utc), Is.InstanceOf<DateTimeOffset>(), "one naming its zone is a moment");
        });
    }

    [Test]
    public void AZonedFormsOwnText_KeepsWhatItDoesNotShow()
    {
        var held = new DateTimeOffset(2026, 10, 1, 14, 30, 45, TimeSpan.FromHours(3));

        Assert.Multiple(() =>
        {
            Assert.That(Read("05.10.2026 16:00 +03:00", "dd.MM.yyyy HH:mm zzz", held),
                Is.EqualTo(new DateTimeOffset(2026, 10, 5, 16, 0, 45, TimeSpan.FromHours(3))));
            Assert.That(Read("16:00 +03:00", "HH:mm zzz", held),
                Is.EqualTo(new DateTimeOffset(2026, 10, 1, 16, 0, 45, TimeSpan.FromHours(3))), "the held day, not today");
        });
    }

    [Test]
    public void ACustomForm_KeepsEachPartItDoesNotShow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read("November 2026", "MMMM yyyy", new DateTime(2026, 10, 19)), Is.EqualTo(new DateTime(2026, 11, 19)),
                "the day stays");
            Assert.That(Read("05.10", "dd.MM", new DateTime(1999, 3, 3)), Is.EqualTo(new DateTime(1999, 10, 5)), "the year stays");
            Assert.That(Read("05.10.30", "dd.MM.yy", new DateTime(1930, 1, 1)), Is.EqualTo(new DateTime(1930, 10, 5)),
                "the century stays");
            Assert.That(Read("3:30", "h:mm", new DateTime(2026, 1, 1, 15, 0, 0)), Is.EqualTo(new DateTime(2026, 1, 1, 15, 30, 0)),
                "the afternoon stays");
            Assert.That(Read("12:10", "h:mm", new DateTime(2026, 1, 1, 9, 0, 0)), Is.EqualTo(new DateTime(2026, 1, 1, 0, 10, 0)));
        });
    }

    [Test]
    public void ATimeAloneOverADayAndTime_KeepsTheDay()
    {
        var was = Languages.Current;
        try
        {
            Languages.Current = "en-US";
            var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var held = new DateTime(1999, 3, 3, 9, 0, 0);
            object Loose(string text) =>
                DateTimeValues.TryParseMoment(text, DateTimeFieldFormat.ShortDateTime, null, culture, held, out var value) ? value : null;

            Assert.Multiple(() =>
            {
                Assert.That(Loose("2:30 PM"), Is.EqualTo(new DateTime(1999, 3, 3, 14, 30, 0)));
                Assert.That(Loose("Oct 5"), Is.EqualTo(new DateTime(1999, 10, 5, 9, 0, 0)), "nor a year it was not given");
            });
        }
        finally
        {
            Languages.Current = was;
        }
    }

    [Test]
    public void AFilterGreaterThan_ComparesWhatTheColumnShows()
    {
        var days = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.ShortDate };
        var times = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.ShortTime };
        var after = new DataGridColumnFilter();
        after.First.Operator = DataGridFilterOperator.IsGreaterThan;
        after.First.Value = days.TextOf(new DateTime(2026, 10, 20));
        var later = new DataGridColumnFilter();
        later.First.Operator = DataGridFilterOperator.IsGreaterThan;
        later.First.Value = times.TextOf(new DateTime(2026, 1, 1, 10, 0, 0));
        bool Passes(DataGridColumnFilter filter, DataGridColumn column, DateTime value) =>
            filter.Passes(column.ComparedAs(value), column.TextOf(value), column.ReadsTyped);

        Assert.Multiple(() =>
        {
            Assert.That(Passes(after, days, new DateTime(2026, 10, 20, 15, 0, 0)), Is.False, "the same day is not after it");
            Assert.That(Passes(after, days, new DateTime(2026, 10, 21)), Is.True);
            Assert.That(Passes(later, times, new DateTime(2020, 1, 1, 11, 0, 0)), Is.True, "any day's 11:00 is past 10:00");
            Assert.That(Passes(later, times, new DateTime(2030, 1, 1, 9, 0, 0)), Is.False);
        });
    }

    private PropertyGrid AtTheDefaultMoment(Record first, Record second)
    {
        var section = new PropertySection { Header = "Times", IsExpanded = true };
        section.Properties.Add(new DateTimeProperty
        {
            Header = "Stamp", Binding = new Binding(nameof(Record.Stamp)),
            DefaultValue = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        });
        var grid = new PropertyGrid();
        grid.Sections.Add(section);
        grid.SelectedObjects = new object[] { first, second };
        return Shown(grid);
    }

    [Test]
    public void ObjectsAtTheDefaultMomentInTheirOwnOffsets_AreAtTheDefault()
    {
        var first = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(3)) };
        var second = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(5)) };
        var row = Rows(AtTheDefaultMoment(first, second)).First();

        Assert.Multiple(() =>
        {
            Assert.That(row.Targets, Has.Count.EqualTo(2));
            Assert.That(row.IsModified, Is.False, "nothing to put back");
            Assert.That(row.IsMixed, Is.False);
        });
    }

    [Test]
    public void AMomentPickedOverAnUnsetOne_IsInThisMachinesOffset()
    {
        var picked = new DateTime(2026, 10, 5, 10, 0, 0);

        Assert.That(DateTimeValues.FromDateTime(picked, typeof(DateTimeOffset), default(DateTimeOffset)),
            Is.EqualTo(new DateTimeOffset(picked, TimeZoneInfo.Local.GetUtcOffset(picked))));
    }

    [Test]
    public void ACalendarsToday_IsADayNotAMomentHere()
    {
        var calendar = Shown(new Calendar());

        Assert.That(calendar.DisplayDate.Kind, Is.EqualTo(DateTimeKind.Unspecified));
    }

    [Test]
    public void ANewFormat_KeepsTheRowsTheFilterTicked_AndConditionsItCannotSay()
    {
        var record = new Record();
        var other = new Record { Due = new DateTime(2027, 5, 1, 8, 0, 0) };
        var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)), Format = DateTimeFieldFormat.ShortDate };
        var grid = new TreeDataGrid();
        dates.Width = new GridLength(180);
        grid.Columns.Add(dates);
        grid.ItemsSource = new List<Record> { record, other };
        Shown(grid);
        var filter = grid.FilterFor(dates);
        filter.Included = new HashSet<string> { dates.TextOf(record.Due) };
        filter.First.Operator = DataGridFilterOperator.IsLessThan;
        var typed = dates.TextOf(new DateTime(2030, 1, 1));
        filter.First.Value = typed;
        grid.ApplyFilters();
        Settle();

        dates.Format = DateTimeFieldFormat.LongDate;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(filter.Included, Is.EquivalentTo(new[] { dates.TextOf(record.Due) }));
            Assert.That(filter.First.Value, Is.EqualTo(dates.TextOf(new DateTime(2030, 1, 1))), "a long date says no more");
            Assert.That(grid.CellFor(0, 0), Is.Not.Null, "the row it let through is still there");
        });

        var longText = filter.First.Value;
        dates.Format = DateTimeFieldFormat.ShortDateTime;
        Settle();
        Assert.That(filter.First.Value, Is.EqualTo(longText), "a time it was not given is not made up");
    }

    [Test]
    public void AnEditOverObjectsAtTheDefault_MovesThemAsOneMoment()
    {
        var first = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(3)) };
        var second = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(5)) };
        var grid = AtTheDefaultMoment(first, second);

        Within<DateTimePickerBase>(Rows(grid).First()).First().Value = new DateTime(2026, 1, 1, 10, 0, 0);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(first.Stamp.UtcDateTime, Is.EqualTo(new DateTime(2026, 1, 1, 10, 0, 0)),
                "the row shows the default, 06:00 at +00:00 - and 10:00 is picked in that frame");
            Assert.That(second.Stamp, Is.EqualTo(first.Stamp), "the same moment");
            Assert.That(second.Stamp.Offset, Is.EqualTo(TimeSpan.FromHours(5)), "in its own offset");
        });
    }

    [Test]
    public void LooseText_KeepsTheTimeAndTheYearItDoesNotSay()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        object Loose(string text, DateTimeFieldFormat format, object held) =>
            DateTimeValues.TryParseMoment(text, format, null, culture, held, out var value) ? value : null;

        Assert.Multiple(() =>
        {
            Assert.That(Loose("Tuesday, March 5, 2024", DateTimeFieldFormat.ShortDateTime, new DateTime(2024, 1, 1, 14, 30, 45)),
                Is.EqualTo(new DateTime(2024, 3, 5, 14, 30, 45)));
            Assert.That(Loose("2/29", DateTimeFieldFormat.ShortDate, new DateTime(2024, 1, 10)), Is.EqualTo(new DateTime(2024, 2, 29)),
                "a leap day, whatever this year is");
            Assert.That(Loose("2/29", DateTimeFieldFormat.ShortDate, new DateTime(2023, 1, 10)), Is.EqualTo(new DateTime(2023, 2, 28)));
            Assert.That(Loose("10/7 2 PM", DateTimeFieldFormat.ShortDateTime, new DateTime(2024, 3, 15)), Is.Null,
                "no form of the language says that - refused, not guessed");
            Assert.That(Loose("3/5", DateTimeFieldFormat.ShortDate, new DateTime(2020, 1, 1)), Is.EqualTo(new DateTime(2020, 3, 5)));
            Assert.That(Read("3/5/99", "M/d/yy", null), Is.EqualTo(new DateTime(1999, 3, 5)), "the language's window with nothing held");
        });
    }

    [Test]
    public void ADatePastTheLanguagesCalendar_ReadsAsSomething()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");

        Assert.That(DateTimeValues.Text(new DateTime(1850, 1, 1), DateTimeFieldFormat.ShortDate, null, culture), Is.Not.Empty);
    }

    [Test]
    public void TheFilterAndTheGroups_ReadWhatIsShown()
    {
        var zoned = DateTimeValues.Cut(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(3)), DateTimeFieldFormat.Custom,
            "h:mm tt zzz");
        var later = DateTimeValues.Cut(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), DateTimeFieldFormat.Custom,
            "h:mm tt zzz");
        var afternoon = DateTimeValues.Cut(new DateTime(2026, 1, 1, 14, 30, 0), DateTimeFieldFormat.Custom, "h:mm");
        var three = DateTimeValues.Cut(new DateTime(2026, 1, 1, 3, 0, 0), DateTimeFieldFormat.Custom, "h:mm");
        string Key(DateTime day) => DateTimeValues.KeyOf(day, DateTimeFieldFormat.Custom, "dddd");
        var lengths = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)), Parts = DurationParts.Hours | DurationParts.Minutes };

        Assert.Multiple(() =>
        {
            Assert.That(((IComparable)zoned).CompareTo(later), Is.LessThan(0), "08:00Z is before 10:00Z");
            Assert.That(((IComparable)afternoon).CompareTo(three), Is.LessThan(0), "2:30 is before 3:00 on a twelve-hour face");
            Assert.That(Key(new DateTime(2024, 6, 3)), Is.Not.EqualTo(Key(new DateTime(2024, 7, 3))), "a Monday and a Wednesday");
            Assert.That(Key(new DateTime(2024, 6, 3)), Is.EqualTo(Key(new DateTime(2024, 6, 10))), "two Mondays");
            Assert.That(lengths.ComparedAs(new TimeSpan(1, 30, 45)), Is.EqualTo(new TimeSpan(1, 30, 0)), "01:30 is not past 01:30");
        });
    }

    [Test]
    public void AFractionsOwnText_KeepsTheTicksBelowIt()
    {
        var held = new DateTime(2026, 1, 1, 10, 0, 45).AddTicks(1234567);

        Assert.That(Read("10:00:45.12", "HH:mm:ss.ff", held), Is.EqualTo(held));
    }

    public sealed class Kinds
    {
        public DateTime When { get; set; }
    }

    [Test]
    public void ClocksOfDifferentKinds_AreTwoValues_UnlessEachIsAtTheDefault()
    {
        var local = new Kinds { When = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Local) };
        var utc = new Kinds { When = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        var kinds = Shown(new PropertyGrid { AutoGenerateSections = true, SelectedObjects = new object[] { local, utc } });
        var kindsRow = Rows(kinds).First(r => Equals(r.Definition?.Header, "When"));
        Assert.That(kindsRow.IsMixed, Is.True, "a local 10:00 and a UTC 10:00 are not one value");

        var first = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(3)) };
        var second = new Record { Stamp = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(5)) };
        var section = new PropertySection { Header = "Times", IsExpanded = true };
        section.Properties.Add(new DateTimeProperty
        {
            Header = "Stamp", Binding = new Binding(nameof(Record.Stamp)), DefaultValue = "2026-01-01T10:00",
        });
        var grid = new PropertyGrid();
        grid.Sections.Add(section);
        grid.SelectedObjects = new object[] { first, second };
        Shown(grid);

        var row = Rows(grid).First();
        Assert.Multiple(() =>
        {
            Assert.That(row.IsMixed, Is.False, "each holds the default's 10:00 at its own offset");
            Assert.That(row.IsModified, Is.False);
        });

        Within<DateTimePickerBase>(row).First().Value = new DateTime(2026, 1, 1, 11, 0, 0);
        Settle();
        Assert.Multiple(() =>
        {
            Assert.That(first.Stamp, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(3))),
                "a clock picked over a clock default is each one's clock, not one moment");
            Assert.That(second.Stamp, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(5))));
        });
    }

    [Test]
    public void LooseText_SaysItsOwnYearAndTime_InAnyLanguage()
    {
        object Loose(string text, string language, DateTimeFieldFormat format, string custom, object held) =>
            DateTimeValues.TryParseMoment(text, format, custom, System.Globalization.CultureInfo.GetCultureInfo(language),
                held, out var value) ? value : null;

        Assert.Multiple(() =>
        {
            Assert.That(Loose("Wednesday, March 5, 2031", "en-US", DateTimeFieldFormat.Custom, "dd.MM.yy", new DateTime(1990, 1, 1)),
                Is.EqualTo(new DateTime(2031, 3, 5)), "a year typed whole is not put in the held century");
            Assert.That(Loose("5.3.2024", "fi-FI", DateTimeFieldFormat.ShortDateTime, null, new DateTime(2020, 1, 1, 14, 30, 0)),
                Is.EqualTo(new DateTime(2024, 3, 5, 14, 30, 0)), "dots are a date here, not a time");
            Assert.That(Loose("5.3.24", "fi-FI", DateTimeFieldFormat.ShortDate, null, new DateTime(2020, 1, 1)),
                Is.EqualTo(new DateTime(2024, 3, 5)));
            Assert.That(Loose("5 March", "en-GB", DateTimeFieldFormat.ShortDate, null, new DateTime(2020, 1, 1)),
                Is.EqualTo(new DateTime(2020, 3, 5)), "no year said, the held one stays");
            Assert.That(Loose("5.10. 14.30", "fi-FI", DateTimeFieldFormat.ShortDateTime, null, new DateTime(2020, 1, 1)), Is.Null,
                "a time read where a date was meant is refused, not written as nothing changed");
        });
    }

    [Test]
    public void AFormThatTellsMore_KeepsTheConditionAsTyped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DateTimeValues.ShowsNoMore(DateTimeFieldFormat.Custom, "HH:mm", DateTimeFieldFormat.Custom, "h:mm"), Is.False,
                "3:00 was both 03:00 and 15:00");
            Assert.That(DateTimeValues.ShowsNoMore(DateTimeFieldFormat.Custom, "dd.MM.yyyy", DateTimeFieldFormat.Custom, "dd.MM.yy"),
                Is.False);
            Assert.That(DateTimeValues.ShowsNoMore(DateTimeFieldFormat.Custom, "dddd", DateTimeFieldFormat.Custom, "dd.MM"), Is.False);
            Assert.That(DateTimeValues.ShowsNoMore(DateTimeFieldFormat.Custom, "dd.MM", DateTimeFieldFormat.Custom, "dd.MM.yyyy"),
                Is.True);
        });
    }

    public class BaseRow
    {
        public DateTime At { get; set; }
    }

    public sealed class HidingRow : BaseRow
    {
        public new TimeOnly At { get; set; }
    }

    [Test]
    public void TheNewRowOfAnEmptyTable_ReadsAHiddenMemberAsTheRowDeclaresIt()
    {
        var times = new DataGridDateTimeColumn { Binding = new Binding(nameof(HidingRow.At)) };
        var grid = new TreeDataGrid { ItemsSource = new List<HidingRow>() };
        grid.Columns.Add(times);

        Assert.That(times.EditingTemplateFor(null).Build(null).RootComponent, Is.InstanceOf<TimePicker>());
    }

    [Test]
    public void ObjectsResetToADefault_EachInItsOwnOffset_AreAtTheDefault()
    {
        foreach (var fallback in new object[] { "2026-01-01T10:00", new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero) })
        {
            var first = new Record { Stamp = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.FromHours(3)) };
            var second = new Record { Stamp = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.FromHours(5)) };
            var section = new PropertySection { Header = "Times", IsExpanded = true };
            section.Properties.Add(new DateTimeProperty
            {
                Header = "Stamp", Binding = new Binding(nameof(Record.Stamp)), DefaultValue = fallback,
            });
            var grid = new PropertyGrid();
            grid.Sections.Add(section);
            grid.SelectedObjects = new object[] { first, second };
            Shown(grid);
            var row = Rows(grid).First();

            Assert.That(row.ResetToDefault(), Is.True);
            Settle();

            Assert.Multiple(() =>
            {
                Assert.That(row.IsMixed, Is.False, fallback.ToString());
                Assert.That(row.IsModified, Is.False, fallback.ToString());
            });
        }
    }

    [Test]
    public void TheGroupsOfACalendarOfItsOwn_AreItsMonths()
    {
        var was = Languages.Current;
        try
        {
            Languages.Current = "ar-SA";
            string Key(DateTime day) => DateTimeValues.KeyOf(day, DateTimeFieldFormat.Custom, "MMMM yyyy");

            Assert.That(Key(new DateTime(2026, 3, 1)), Is.Not.EqualTo(Key(new DateTime(2026, 3, 25))), "Ramadan and Shawwal");
        }
        finally
        {
            Languages.Current = was;
        }
    }

    [Test]
    public void ALengthOfDaysAlone_HasNoSpaceAtItsEnd()
    {
        Assert.That(DateTimeValues.DurationLine(new TimeSpan(2, 5, 0, 0), DurationParts.Days, "d",
            System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("2 d"));
    }

    public interface IDated
    {
        TimeOnly Due { get; }
    }

    public interface IRow : IDated;

    [Test]
    public void TheNewRowOfAnEmptyTable_ReadsAMemberAnInterfaceDeclares()
    {
        var times = new DataGridDateTimeColumn { Binding = new Binding(nameof(IDated.Due)) };
        var grid = new TreeDataGrid { ItemsSource = new System.Collections.ObjectModel.ObservableCollection<IRow>() };
        grid.Columns.Add(times);

        Assert.That(times.EditingTemplateFor(null).Build(null).RootComponent, Is.InstanceOf<TimePicker>());
    }

    [Test]
    public void AFormWithAWeekdayButNoYear_IsCheckedAgainstTheHeldYear()
    {
        var held = new DateTime(2026, 1, 1);
        var thai = System.Globalization.CultureInfo.GetCultureInfo("th-TH");

        Assert.Multiple(() =>
        {
            Assert.That(Read("Tue 20.10", "ddd dd.MM", held), Is.EqualTo(new DateTime(2026, 10, 20)));
            Assert.That(Read("Fri 20.10", "ddd dd.MM", held), Is.Null, "the 20th of October 2026 is a Tuesday");
            Assert.That(DateTimeValues.TryParseMoment("29/2", DateTimeFieldFormat.Custom, "d/M", thai, new DateTime(2024, 1, 10),
                out var leap) ? leap : null, Is.EqualTo(new DateTime(2024, 2, 29)), "a leap day in a calendar of its own");
        });
    }

    [Test]
    public void TextADateColumnRefuses_IsNotWrittenAnyway()
    {
        var dates = new DataGridDateTimeColumn { Binding = new Binding(nameof(Record.Due)) };

        Assert.That(dates.ValueOfText("Oct 3 then", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Is.SameAs(AdamantiumProperty.UnsetValue));
    }

    public sealed class Loose
    {
        public object When { get; set; } = new DateTime(2026, 1, 1);

        public object Length { get; set; } = TimeSpan.FromHours(1);
    }

    [Test]
    public void RefusedText_LeavesAMemberOfNoTypeAsItWas()
    {
        var row = new Loose();
        var grid = new TreeDataGrid();
        foreach (var column in new DataGridColumn[]
                 {
                     new DataGridDateTimeColumn { Binding = new Binding(nameof(Loose.When)) },
                     new DataGridDurationColumn { Binding = new Binding(nameof(Loose.Length)) },
                 })
        {
            column.Width = new GridLength(180);
            grid.Columns.Add(column);
        }

        grid.ItemsSource = new List<Loose> { row };
        Shown(grid);

        Assert.Multiple(() =>
        {
            foreach (var at in new[] { 0, 1 })
            {
                var peer = (Adamantium.UI.Controls.Automation.DataGridCellAutomationPeer)grid.CellFor(0, at).GetAutomationPeer();
                Assert.Throws<InvalidOperationException>(() => peer.SetValue("soon"));
            }

            Assert.That(row.When, Is.EqualTo(new DateTime(2026, 1, 1)));
            Assert.That(row.Length, Is.EqualTo(TimeSpan.FromHours(1)));
        });
    }

    [Test]
    public void TimeTextOverAnUnsetDay_ReadsInACalendarOfItsOwn()
    {
        var persian = System.Globalization.CultureInfo.GetCultureInfo("fa-IR");

        Assert.That(DateTimeValues.TryParseMoment("28/07", DateTimeFieldFormat.Custom, "dd/MM", persian, default(DateTime), out _),
            Is.True);
    }

    [Test]
    public void AnEmptyDefault_IsReadByItsLine()
    {
        var line = new ImageSourceProperty { DefaultValue = "" };

        Assert.That(line.TryReadDefault(null, out var value) ? value : "refused", Is.Null);
    }

    [Test]
    public void AWordForDays_IsNotGivenToAColumnWithoutDays()
    {
        var record = new Record { Length = TimeSpan.FromHours(30) };
        var lengths = new DataGridDurationColumn { Binding = new Binding(nameof(Record.Length)), Parts = DurationParts.Hours };
        var grid = Table(record, lengths);
        var filter = grid.FilterFor(lengths);
        filter.Included = new HashSet<string> { lengths.TextOf(record.Length) };
        grid.ApplyFilters();
        Settle();

        lengths.DayUnit = "d";
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(filter.Included, Is.EquivalentTo(new[] { "30" }));
            Assert.That(grid.CellFor(0, 0), Is.Not.Null);
        });
    }

    [Test]
    public void ANumberDefault_IsReadInNoLanguage()
    {
        var was = Languages.Current;
        try
        {
            Languages.Current = "de-DE";
            var line = new NumericProperty { DefaultValue = "2.5" };

            Assert.That(line.TryReadDefault(typeof(double), out var value) ? value : null, Is.EqualTo(2.5));
        }
        finally
        {
            Languages.Current = was;
        }
    }

    public sealed class Clocked
    {
        public TimeOnly At { get; set; }
    }

    [Test]
    public void TheNewRowOfAnEmptyTable_EditsATimeInATimePicker()
    {
        var times = new DataGridDateTimeColumn { Binding = new Binding(nameof(Clocked.At)) };
        var grid = new TreeDataGrid { ItemsSource = new List<Clocked>() };
        grid.Columns.Add(times);

        Assert.That(times.EditingTemplateFor(null).Build(null).RootComponent, Is.InstanceOf<TimePicker>());
    }
}
