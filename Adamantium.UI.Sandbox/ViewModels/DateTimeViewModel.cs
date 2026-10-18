using System;
using Adamantium.MVVM;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Date and time tab: calendars - one bound to <see cref="SelectedDate"/> with its selection mode, week numbers
/// and language switched, one given a large size its cells share, one with a range and days struck through.</summary>
[ViewModel]
public partial class DateTimeViewModel : TabPageViewModel
{
    public DateTimeViewModel() : base("DateTime")
    {
    }

    [Bindable] private DateTime? _selectedDate = DateTime.Today;

    [Bindable] private CalendarSelectionMode _selectionMode = CalendarSelectionMode.SingleDate;

    [Bindable] private bool _showWeekNumbers = true;

    [Bindable] private string _calendarLanguage = "ru-RU";

    /// <summary>The first day the range calendar shows: today.</summary>
    public DateTime RangeStart => DateTime.Today;

    /// <summary>The last day the range calendar shows: three months on.</summary>
    public DateTime RangeEnd => DateTime.Today.AddMonths(3);

    [Command] private void ShowLanguage(object language) => CalendarLanguage = language as string;
}
