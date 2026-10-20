using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>One part of a <see cref="DateTimeField"/> - the day, the month, the hours.</summary>
public class DateTimeFieldSegment : FieldSegment
{
    public static readonly AdamantiumProperty KindProperty = AdamantiumProperty.RegisterReadOnly(nameof(Kind),
        typeof(DateTimeSegmentKind), typeof(DateTimeFieldSegment), new PropertyMetadata(DateTimeSegmentKind.Day));

    public DateTimeSegmentKind Kind
    {
        get => GetValue<DateTimeSegmentKind>(KindProperty);
        internal set => SetValue(KindProperty, value);
    }
}
