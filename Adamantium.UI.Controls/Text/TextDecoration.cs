using System;
using System.Collections.Specialized;
using Adamantium.Core.Collections;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>A line drawn with an inline, as WPF's text decoration: where it runs, its brush, thickness and dashes, and
/// how far it is moved from its place. <c>&lt;TextDecoration Location="Overline" Brush="Red" Thickness="2"
/// DashArray="4 2"/&gt;</c>.</summary>
public sealed class TextDecoration : AdamantiumComponent
{
    private TrackingCollection<double> _dashes;

    public static readonly AdamantiumProperty LocationProperty = AdamantiumProperty.Register(nameof(Location),
        typeof(TextDecorationLocation), typeof(TextDecoration),
        new PropertyMetadata(TextDecorationLocation.Underline, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty BrushProperty = AdamantiumProperty.Register(nameof(Brush),
        typeof(Brush), typeof(TextDecoration), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ThicknessProperty = AdamantiumProperty.Register(nameof(Thickness),
        typeof(double), typeof(TextDecoration), new PropertyMetadata(double.NaN, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty OffsetProperty = AdamantiumProperty.Register(nameof(Offset),
        typeof(double), typeof(TextDecoration), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty DashArrayProperty = AdamantiumProperty.Register(nameof(DashArray),
        typeof(TrackingCollection<double>), typeof(TextDecoration),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Under the text (the default), over it, through it or on the baseline.</summary>
    public TextDecorationLocation Location
    {
        get => GetValue<TextDecorationLocation>(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    /// <summary>A solid color brush the line is drawn in; null takes the text's color. A theme's brush comes through
    /// <c>{ObservableResource}</c>, which follows a theme swap.</summary>
    public Brush Brush
    {
        get => GetValue<Brush>(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>The line's thickness; NaN (the default) takes the font's for its place.</summary>
    public double Thickness
    {
        get => GetValue<double>(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>How far the line is moved from its place: down in horizontal text.</summary>
    public double Offset
    {
        get => GetValue<double>(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>The lengths of the line's dashes and the gaps between them, in turn; null draws it solid.</summary>
    public TrackingCollection<double> DashArray
    {
        get => GetValue<TrackingCollection<double>>(DashArrayProperty);
        set => SetValue(DashArrayProperty, value);
    }

    internal event EventHandler Changed;

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == DashArrayProperty)
        {
            if (_dashes != null)
            {
                _dashes.CollectionChanged -= OnDashesChanged;
            }

            _dashes = DashArray;
            if (_dashes != null)
            {
                _dashes.CollectionChanged += OnDashesChanged;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDashesChanged(object sender, NotifyCollectionChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    internal TextDecorationLine ToLine() => new()
    {
        Location = Location,
        Thickness = double.IsNaN(Thickness) ? null : Thickness,
        Offset = Offset,
        Color = (Brush as SolidColorBrush)?.Color,
        Dashes = DashArray is { Count: > 0 } dashes ? [..dashes] : null,
    };
}
