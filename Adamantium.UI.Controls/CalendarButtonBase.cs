using System;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>A cell of a <see cref="Calendar"/> - a day, a month or a year. The calendar makes and fills them; the style
/// shows them. Its <c>Padding</c> is the room it asks for around its content: given less, it shrinks into the room
/// rather than squeezing the content.</summary>
public abstract class CalendarButtonBase : ButtonBase
{
    public static readonly AdamantiumProperty DateProperty = AdamantiumProperty.RegisterReadOnly(nameof(Date),
        typeof(DateTime), typeof(CalendarButtonBase), new PropertyMetadata(default(DateTime)));

    public static readonly AdamantiumProperty IsInactiveProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsInactive),
        typeof(bool), typeof(CalendarButtonBase), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsCursorProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsCursor),
        typeof(bool), typeof(CalendarButtonBase), new PropertyMetadata(false));

    private protected CalendarButtonBase()
    {
        Focusable = false;
    }

    /// <summary>The day, or the first day of the month or of the year.</summary>
    public DateTime Date
    {
        get => GetValue<DateTime>(DateProperty);
        internal set => SetValue(DateProperty, value);
    }

    /// <summary>Outside the month or the decade shown, or past the first or last day there is.</summary>
    public bool IsInactive
    {
        get => GetValue<bool>(IsInactiveProperty);
        internal set => SetValue(IsInactiveProperty, value);
    }

    /// <summary>The cell the keyboard is on, while the calendar's focus is shown.</summary>
    public bool IsCursor
    {
        get => GetValue<bool>(IsCursorProperty);
        internal set => SetValue(IsCursorProperty, value);
    }

    internal Calendar Owner { get; set; }

    internal int Index { get; set; }

    internal int Row => Index / Columns();

    internal int Column => Index % Columns();

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = base.MeasureOverride(availableSize);
        var padding = Padding;
        return new Size(content.Width + padding.Left + padding.Right, content.Height + padding.Top + padding.Bottom);
    }

    private protected abstract int Columns();
}
