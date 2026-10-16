using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Themes.Localization;

namespace Adamantium.UI.Text;

/// <summary>Checks the spelling of the text box it is attached to: marks its misspelled words and, at the top of the
/// box's menu, offers for the word under the pointer (or at the caret) what it may have been meant as, to ignore it or
/// to add it to the dictionary. The text is checked off the UI thread, the latest text winning; the marks follow when
/// the check is done. <see cref="SpellCheck.IsEnabled"/> attaches it.</summary>
public class SpellCheckBehavior : Behavior<TextBox>
{
    private const int MostSuggestions = 5;
    private const string Fallback = "en";

    private readonly List<object> _rows = [];
    private TextBox _box;
    private ContextMenu _menu;
    private ISpellChecker _checker;
    private string _markedText = string.Empty;
    private int _version;
    private bool _checking;

    /// <summary>The checker used; null takes the application's (the system's spelling, by default).</summary>
    public ISpellChecker Checker
    {
        get => _checker;
        set
        {
            _checker = value;
            Recheck();
        }
    }

    /// <summary>Whether the text is checked off the UI thread (default). Off, every change is checked at once, as a
    /// quick checker of an application's own words can afford.</summary>
    public bool ChecksInBackground { get; set; } = true;

    protected override void OnAttached(TextBox box)
    {
        _box = box;
        box.PropertyChanged += OnBoxChanged;
        Watch(box.ContextMenu);
        Recheck();
    }

    protected override void OnDetached(TextBox box)
    {
        box.PropertyChanged -= OnBoxChanged;
        RemoveRows();
        Watch(null);
        _version++;
        box.MisspelledRanges = [];
        _box = null;
    }

    private void OnBoxChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            Mark(_box.MisspelledRanges, _markedText);
            Recheck();
        }
        else if (e.Property == UIComponent.LanguageProperty)
        {
            Recheck();
        }
        else if (e.Property == InputUIComponent.ContextMenuProperty)
        {
            Watch(_box?.ContextMenu);
        }
    }

    private void Watch(ContextMenu menu)
    {
        if (_menu != null)
        {
            _menu.Opening -= OnMenuOpening;
            _menu.PropertyChanged -= OnMenuChanged;
            RemoveRows();
        }

        _menu = menu;
        if (_menu != null)
        {
            _menu.Opening += OnMenuOpening;
            _menu.PropertyChanged += OnMenuChanged;
        }
    }

    private void OnMenuChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContextMenu.IsOpenProperty && e.NewValue is false)
        {
            RemoveRows();
        }
    }

    private void Recheck()
    {
        if (_box == null)
        {
            return;
        }

        var version = ++_version;
        var text = _box.Text ?? string.Empty;
        if (SpellChecker() is not { } checker)
        {
            Mark([], text);
            return;
        }

        var language = Language();
        if (!ChecksInBackground || Threading.Dispatcher.CurrentDispatcher is not { UIThread: { } loop } dispatcher
            || loop != Thread.CurrentThread)
        {
            Mark(Checked(checker, text, language), text);
            return;
        }

        if (_checking)
        {
            return;
        }

        _checking = true;
        Task.Run(() => Checked(checker, text, language)).ContinueWith(done => dispatcher.Post(() =>
        {
            _checking = false;
            if (_box == null)
            {
                return;
            }

            Mark(done.Result, text);
            if (version != _version)
            {
                Recheck();
            }
        }), TaskScheduler.Default);
    }

    private void Mark(IReadOnlyList<TextRange> marks, string markedIn)
    {
        var now = _box.Text ?? string.Empty;
        _markedText = now;
        _box.MisspelledRanges = Moved(marks, markedIn, now);
    }

    internal static IReadOnlyList<TextRange> Moved(IReadOnlyList<TextRange> marks, string markedIn, string now)
    {
        if (markedIn == now)
        {
            return marks;
        }

        var head = 0;
        var most = Math.Min(markedIn.Length, now.Length);
        while (head < most && markedIn[head] == now[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < most - head && markedIn[markedIn.Length - 1 - tail] == now[now.Length - 1 - tail])
        {
            tail++;
        }

        var changedEnd = markedIn.Length - tail;
        var shift = now.Length - markedIn.Length;
        List<TextRange> moved = [];
        foreach (var mark in marks)
        {
            if (mark.Start < 0 || mark.End > markedIn.Length)
            {
                continue;
            }

            if (mark.End < head)
            {
                moved.Add(mark);
            }
            else if (mark.Start > changedEnd)
            {
                moved.Add(new TextRange(mark.Start + shift, mark.Length));
            }
        }

        return moved;
    }

    private static IReadOnlyList<TextRange> Checked(ISpellChecker checker, string text, string language)
    {
        try
        {
            return checker.Check(text, language) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void OnMenuOpening(object sender, ContextMenuOpeningEventArgs e)
    {
        RemoveRows();
        if (_box == null || !ReferenceEquals(e.Target, _box) || SpellChecker() is not { } checker)
        {
            return;
        }

        var text = _box.Text ?? string.Empty;
        var pointed = e.Point is { } point;
        var at = pointed ? _box.GetCharacterIndexFromPoint(e.Point.Value) : _box.CaretIndex;
        var misspelled = _box.MisspelledRanges.FirstOrDefault(range =>
            range.Start <= at && (pointed ? at < range.End : at <= range.End));
        if (misspelled.Length == 0 || misspelled.End > text.Length)
        {
            return;
        }

        var word = text.Substring(misspelled.Start, misspelled.Length);
        var language = Language();
        if (!_box.IsReadOnly)
        {
            var room = _box.MaxLength > 0 ? _box.MaxLength - (text.Length - word.Length) : int.MaxValue;
            var suggestions = Suggested(checker, word, language)
                .Where(suggestion => suggestion.Length <= room)
                .Take(MostSuggestions)
                .ToList();
            foreach (var suggestion in suggestions)
            {
                AddRow(Row(suggestion, () => Correct(misspelled, word, suggestion)));
            }

            if (suggestions.Count == 0)
            {
                var none = Row(TextBoxStrings.NoSuggestions, null);
                none.IsEnabled = false;
                AddRow(none);
            }
        }

        AddRow(Row(TextBoxStrings.IgnoreAll, () => Learned(() => checker.IgnoreAll(word, language))));
        AddRow(Row(TextBoxStrings.AddToDictionary, () => Learned(() => checker.AddToDictionary(word, language))));
        AddRow(new Separator());
    }

    private void Correct(TextRange misspelled, string word, string suggestion)
    {
        var text = _box?.Text ?? string.Empty;
        if (misspelled.End <= text.Length && string.CompareOrdinal(text, misspelled.Start, word, 0, word.Length) == 0)
        {
            _box.Replace(misspelled, suggestion);
        }
    }

    private void Learned(Action learn)
    {
        try
        {
            learn();
        }
        catch (Exception)
        {
            return;
        }

        Recheck();
    }

    private static IReadOnlyList<string> Suggested(ISpellChecker checker, string word, string language)
    {
        try
        {
            return checker.Suggest(word, language) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static MenuItem Row(string header, Action click)
    {
        var row = new MenuItem { Header = header };
        if (click != null)
        {
            row.Click += (_, _) => click();
        }

        return row;
    }

    private void AddRow(object row)
    {
        _menu.Items.Insert(_rows.Count, row);
        _rows.Add(row);
    }

    private void RemoveRows()
    {
        foreach (var row in _rows)
        {
            _menu?.Items.Remove(row);
        }

        _rows.Clear();
    }

    private ISpellChecker SpellChecker() =>
        _checker ?? (UIApplication.Current?.Container is { } container && container.IsRegistered<ISpellChecker>()
            ? container.Resolve<ISpellChecker>()
            : null);

    private string Language()
    {
        var own = _box?.Language?.Trim();
        return !string.IsNullOrEmpty(own) ? own : Languages.Current ?? Fallback;
    }
}
