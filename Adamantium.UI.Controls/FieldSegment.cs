using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>One part of a field entered part by part - a <see cref="DateTimeFieldSegment"/> of a date, a
/// <see cref="DurationFieldSegment"/> of a duration: its text, or its placeholder while it is empty, and whether the
/// keyboard is on it.</summary>
public abstract class FieldSegment : Control
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.RegisterReadOnly(nameof(Text),
        typeof(string), typeof(FieldSegment), new PropertyMetadata(string.Empty));

    public static readonly AdamantiumProperty IsEmptyProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsEmpty),
        typeof(bool), typeof(FieldSegment), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsActive),
        typeof(bool), typeof(FieldSegment), new PropertyMetadata(false, PropertyMetadataOptions.AffectsParentArrange));

    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(string), typeof(FieldSegment), new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    private string _shown;
    private int _width;

    /// <summary>What it shows: its value, what is being typed into it, or its placeholder.</summary>
    public string Text
    {
        get => GetValue<string>(TextProperty);
        private set => SetValue(TextProperty, value);
    }

    /// <summary>Nothing entered: it shows its placeholder.</summary>
    public bool IsEmpty
    {
        get => GetValue<bool>(IsEmptyProperty);
        private set => SetValue(IsEmptyProperty, value);
    }

    /// <summary>The keyboard is on it: arrows step it, digits go into it.</summary>
    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        internal set => SetValue(IsActiveProperty, value);
    }

    /// <summary>What it shows while empty; a single letter is repeated to the segment's width - <c>д</c> shows as
    /// <c>дд</c>, and as <c>гггг</c> for a four-digit year when the letter is <c>г</c>.</summary>
    public string Placeholder
    {
        get => GetValue<string>(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    internal ISegmentHost Host { get; set; }

    internal int Index { get; set; }

    internal void Show(string text, int width)
    {
        _shown = text;
        _width = width;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FieldSegmentAutomationPeer(this);

    private static void OnPlaceholderChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((FieldSegment)a).Refresh();

    private void Refresh()
    {
        IsEmpty = _shown == null;
        var placeholder = Placeholder ?? string.Empty;
        Text = _shown ?? (placeholder.Length == 1 ? new string(placeholder[0], _width) : placeholder);
    }
}

internal interface ISegmentHost
{
    (long Minimum, long Maximum) RangeOf(FieldSegment segment);

    long? ValueOf(FieldSegment segment);

    bool IsReadOnly(FieldSegment segment);

    void Write(FieldSegment segment, long value);
}
