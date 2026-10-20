using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>One part of a <see cref="DurationField"/> - the days, the hours, the minutes.</summary>
public class DurationFieldSegment : FieldSegment
{
    public static readonly AdamantiumProperty PartProperty = AdamantiumProperty.RegisterReadOnly(nameof(Part),
        typeof(DurationParts), typeof(DurationFieldSegment), new PropertyMetadata(DurationParts.Hours));

    public DurationParts Part
    {
        get => GetValue<DurationParts>(PartProperty);
        internal set => SetValue(PartProperty, value);
    }
}
