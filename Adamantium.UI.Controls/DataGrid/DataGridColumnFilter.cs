using System;
using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>How a filter condition tests a cell's value. The set a spreadsheet offers, and no more: an operator nobody
/// can name from the list is an operator nobody uses.</summary>
public enum DataGridFilterOperator
{
    IsEqualTo,
    IsNotEqualTo,
    Contains,
    DoesNotContain,
    StartsWith,
    EndsWith,
    IsGreaterThan,
    IsLessThan,
    IsEmpty,
    IsNotEmpty
}

/// <summary>How the two conditions of one column filter are joined.</summary>
public enum DataGridFilterLogic
{
    And,
    Or
}

/// <summary>One test on one cell value.</summary>
public class DataGridFilterCondition
{
    public DataGridFilterOperator Operator { get; set; } = DataGridFilterOperator.IsEqualTo;

    /// <summary>What to test against. Text, whatever the column holds - the value is compared as it READS, so a filter
    /// says the same thing as the cell it was typed next to.</summary>
    public object Value { get; set; }

    public bool IsCaseSensitive { get; set; }

    /// <summary>Whether this condition says anything at all. An operator that needs no value (empty / not empty) is set
    /// on its own; every other one is idle until something is typed.</summary>
    public bool IsSet => Operator is DataGridFilterOperator.IsEmpty or DataGridFilterOperator.IsNotEmpty
                         || !string.IsNullOrEmpty(Value?.ToString());

    public void Clear()
    {
        Operator = DataGridFilterOperator.IsEqualTo;
        Value = null;
    }

    /// <summary>Whether <paramref name="value"/> passes this condition.</summary>
    public bool Passes(object value) => Passes(value, value?.ToString());

    /// <summary>Whether <paramref name="value"/>, reading as <paramref name="text"/> in its column, passes this
    /// condition.</summary>
    public bool Passes(object value, string text) => Passes(value, text, null);

    /// <summary>...with what is typed to compare against read as the column reads a paste: "greater than 01:30:00"
    /// compares lengths, not text.</summary>
    public bool Passes(object value, string text, Func<string, object> parse)
    {
        text ??= string.Empty;
        var wanted = Value?.ToString() ?? string.Empty;
        var comparison = IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return Operator switch
        {
            DataGridFilterOperator.IsEmpty => text.Length == 0,
            DataGridFilterOperator.IsNotEmpty => text.Length != 0,
            DataGridFilterOperator.IsEqualTo => string.Equals(text, wanted, comparison),
            DataGridFilterOperator.IsNotEqualTo => !string.Equals(text, wanted, comparison),
            DataGridFilterOperator.Contains => text.Contains(wanted, comparison),
            DataGridFilterOperator.DoesNotContain => !text.Contains(wanted, comparison),
            DataGridFilterOperator.StartsWith => text.StartsWith(wanted, comparison),
            DataGridFilterOperator.EndsWith => text.EndsWith(wanted, comparison),
            DataGridFilterOperator.IsGreaterThan => Compare(value, wanted, comparison, parse) > 0,
            DataGridFilterOperator.IsLessThan => Compare(value, wanted, comparison, parse) < 0,
            _ => true
        };
    }

    private string _readText;
    private Func<string, object> _readBy;
    private object _read;

    /// <summary>Drops what the condition read its value as - for a column whose form or language changed.</summary>
    public void Forget()
    {
        _readText = null;
        _readBy = null;
        _read = null;
    }

    // Numbers and dates compare AS THEMSELVES where they can - "9" is not greater than "10" in a Size column, however
    // convincing the string comparison is.
    private int Compare(object value, string wanted, StringComparison comparison, Func<string, object> parse)
    {
        if (parse != null && (!ReferenceEquals(parse, _readBy) || !string.Equals(wanted, _readText, StringComparison.Ordinal)))
        {
            _readBy = parse;
            _readText = wanted;
            _read = parse(wanted);
        }

        if (parse != null && _read is IComparable parsed and not string)
        {
            var held = parsed is DateTime ? DateTimeValues.ToDateTime(value) : value;
            if (held is IComparable mine && mine.GetType() == parsed.GetType()) return mine.CompareTo(parsed);
        }

        if (value is IComparable comparable && value is not string)
        {
            try
            {
                return comparable.CompareTo(Convert.ChangeType(wanted, value.GetType()));
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
            {
                return 0;
            }
        }

        return string.Compare(value?.ToString() ?? string.Empty, wanted, comparison);
    }
}

/// <summary>What one column is filtered by: a set of values that are let through, and up to two conditions joined by
/// AND or OR - the spreadsheet's own shape, because it is the one every user of a table already knows.
/// <para>The two halves are combined: a row must be in the checked SET and satisfy the conditions.</para></summary>
public class DataGridColumnFilter
{
    /// <summary>The values let through, held as they READ. Null means every value passes, which is not the same as an
    /// empty set - that one lets nothing through, and is what unchecking everything means.</summary>
    public HashSet<string> Included { get; set; }

    public DataGridFilterCondition First { get; } = new();

    public DataGridFilterLogic Logic { get; set; }

    public DataGridFilterCondition Second { get; } = new();

    /// <summary>Whether this filter narrows anything. A column whose filter is inactive is drawn as unfiltered.</summary>
    public bool IsActive => Included != null || First.IsSet || Second.IsSet;

    public void Clear()
    {
        Included = null;
        First.Clear();
        Second.Clear();
        Logic = DataGridFilterLogic.And;
    }

    /// <summary>Whether a cell holding <paramref name="value"/> survives.</summary>
    public bool Passes(object value) => Passes(value, Text(value));

    /// <summary>Whether a cell holding <paramref name="value"/>, reading as <paramref name="text"/> in its column,
    /// survives.</summary>
    public bool Passes(object value, string text) => Passes(value, text, null);

    /// <summary>...with what the conditions compare against read by <paramref name="parse"/>.</summary>
    public bool Passes(object value, string text, Func<string, object> parse)
    {
        if (Included != null && !Included.Contains(text ?? string.Empty)) return false;

        var first = First.IsSet ? First.Passes(value, text, parse) : (bool?)null;
        var second = Second.IsSet ? Second.Passes(value, text, parse) : (bool?)null;

        if (first == null) return second ?? true;
        if (second == null) return first.Value;

        return Logic == DataGridFilterLogic.And ? first.Value && second.Value : first.Value || second.Value;
    }

    /// <summary>How a value is keyed in <see cref="Included"/> and shown in the value list - one rule, so what is ticked
    /// is what is compared.</summary>
    public static string Text(object value) => value?.ToString() ?? string.Empty;
}

/// <summary>One line of the value list: what it reads and whether it is ticked.</summary>
public class DataGridFilterValue : AdamantiumComponent
{
    public static readonly AdamantiumProperty IsCheckedProperty = AdamantiumProperty.Register(nameof(IsChecked),
        typeof(bool), typeof(DataGridFilterValue), new PropertyMetadata(true));

    public DataGridFilterValue(object value, bool isChecked) : this(value, DataGridColumnFilter.Text(value), isChecked)
    {
    }

    /// <summary>A line for <paramref name="value"/> reading as <paramref name="text"/> in its column.</summary>
    public DataGridFilterValue(object value, string text, bool isChecked)
    {
        Value = value;
        Text = text ?? string.Empty;
        IsChecked = isChecked;
    }

    public object Value { get; }

    public string Text { get; }

    public bool IsChecked
    {
        get => GetValue<bool>(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
}
