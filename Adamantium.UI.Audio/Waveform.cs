using Adamantium.Mathematics;
using Adamantium.Pulsar;
using Adamantium.UI.Audio.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Audio;

/// <summary>A sound's waveform that is its position, as players with one have it: bars of the least and the greatest
/// sample of all channels, apart from each other on whole pixels, the played ones in <see cref="PlayedBrush"/>, and a line
/// where it is. Pressed or dragged it goes there; the arrows step by <see cref="RangeLimitsBase.SmallChange"/>, Page by
/// <see cref="RangeLimitsBase.LargeChange"/>, Home and End to the ends. <see cref="RangeBase.Value"/> is the position
/// between <see cref="RangeLimitsBase.Minimum"/> and <see cref="RangeLimitsBase.Maximum"/>, in seconds for a player.
/// Drawn as it is read, a part at a time.</summary>
public class Waveform : RangeBase
{
    private bool _isTicking;
    private bool _isDragging;

    static Waveform()
    {
        AddStyleSetToThemes<Themes.WaveformStyleSet>();
        FocusableProperty.OverrideMetadata(typeof(Waveform), new PropertyMetadata(true));
    }

    public Waveform()
    {
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
    }

    public static readonly AdamantiumProperty PeaksProperty = AdamantiumProperty.Register(nameof(Peaks),
        typeof(SoundWaveform), typeof(Waveform),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnPeaksChanged));

    public static readonly AdamantiumProperty WaveBrushProperty = AdamantiumProperty.Register(nameof(WaveBrush),
        typeof(Brush), typeof(Waveform), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty PlayedBrushProperty = AdamantiumProperty.Register(nameof(PlayedBrush),
        typeof(Brush), typeof(Waveform), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty CursorBrushProperty = AdamantiumProperty.Register(nameof(CursorBrush),
        typeof(Brush), typeof(Waveform), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BarWidthProperty = AdamantiumProperty.Register(nameof(BarWidth),
        typeof(double), typeof(Waveform), new PropertyMetadata(2.0, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BarSpacingProperty = AdamantiumProperty.Register(nameof(BarSpacing),
        typeof(double), typeof(Waveform), new PropertyMetadata(1.0, PropertyMetadataOptions.AffectsRender));

    public SoundWaveform Peaks
    {
        get => GetValue<SoundWaveform>(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    /// <summary>What is yet to be played.</summary>
    public Brush WaveBrush
    {
        get => GetValue<Brush>(WaveBrushProperty);
        set => SetValue(WaveBrushProperty, value);
    }

    public Brush PlayedBrush
    {
        get => GetValue<Brush>(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    /// <summary>The line where it is; none where null.</summary>
    public Brush CursorBrush
    {
        get => GetValue<Brush>(CursorBrushProperty);
        set => SetValue(CursorBrushProperty, value);
    }

    /// <summary>Of each bar, in DIPs; at least a pixel.</summary>
    public double BarWidth
    {
        get => GetValue<double>(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    /// <summary>Between the bars, in DIPs; 0 for a wave of no gaps.</summary>
    public double BarSpacing
    {
        get => GetValue<double>(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WaveformAutomationPeer(this);

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);
        var session = context.ForControl(this);
        var size = RenderSize;
        if (session == null || size.Width < 1 || size.Height <= 0)
        {
            return;
        }

        var range = Maximum - Minimum;
        var playedTo = range > 0 ? (Value - Minimum) / range * size.Width : 0;
        if (Peaks is { } peaks)
        {
            DrawBars(session, peaks, size.Width, size.Height, playedTo);
        }

        if (CursorBrush != null && range > 0)
        {
            var cursor = CursorAt(playedTo, size.Width, size.Height);
            this.Snap(ref cursor);
            session.DrawRectangle(CursorBrush, cursor);
        }
    }

    /// <summary>The line where it is, two DIPs wide, kept within however narrow it is.</summary>
    internal static Rect CursorAt(double playedTo, double width, double height)
    {
        var lineWidth = Math.Min(2, width);
        return new Rect(Math.Clamp(playedTo - lineWidth / 2, 0, width - lineWidth), 0, lineWidth, height);
    }

    // Taken away mid-drag - another window, the waveform hidden: the drag is over, and moving over it seeks no more.
    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        double? to = e.Key switch
        {
            Key.RightArrow or Key.UpArrow => Value + SmallChange,
            Key.LeftArrow or Key.DownArrow => Value - SmallChange,
            Key.PageUp => Value + LargeChange,
            Key.PageDown => Value - LargeChange,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (to is { } value)
        {
            GoTo(value);
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
    }

    /// <summary>For tests: the value a press at <paramref name="x"/> DIPs from the left goes to.</summary>
    internal double ValueAt(double x)
    {
        var width = RenderSize.Width;
        return width > 0 ? Minimum + Math.Clamp(x / width, 0, 1) * (Maximum - Minimum) : Minimum;
    }

    // Bars of whole pixels, apart from each other: at a scale of 1.5 a bar of 1 DIP would fall on half pixels, and the
    // neighbours run into each other.
    private void DrawBars(IDrawingSession session, SoundWaveform peaks, double across, double high, double playedTo)
    {
        var width = Math.Max(this.SnapLength(Math.Max(0.1, BarWidth)), 0.1);
        var spacing = Math.Max(0, this.SnapLength(Math.Max(0, BarSpacing)));
        var bars = (int)((across + spacing) / (width + spacing));
        if (bars < 1)
        {
            return;
        }

        var middle = high / 2;
        var read = peaks.PartsRead;
        for (var bar = 0; bar < bars; bar++)
        {
            var from = (int)((long)bar * peaks.Parts / bars);
            var to = Math.Max(from + 1, (int)((long)(bar + 1) * peaks.Parts / bars));
            if (to > read)
            {
                break;
            }

            var least = 0f;
            var greatest = 0f;
            for (var part = from; part < to; part++)
            {
                for (var channel = 0; channel < peaks.Channels; channel++)
                {
                    least = Math.Min(least, peaks.MinimumOf(channel, part));
                    greatest = Math.Max(greatest, peaks.MaximumOf(channel, part));
                }
            }

            var left = bar * (width + spacing);
            var brush = left + width / 2 < playedTo ? PlayedBrush ?? WaveBrush : WaveBrush;
            if (brush == null)
            {
                continue;
            }

            // A bar of silence is still a line, so the wave reads as one.
            var top = middle - Math.Min(1, greatest) * middle;
            var height = Math.Max(1, (Math.Min(1, greatest) - Math.Max(-1, least)) * middle);
            var rect = new Rect(left, top, width, height);
            this.Snap(ref rect);
            session.DrawRectangle(brush, rect);
        }
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        Focus();
        _isDragging = true;
        e.Pointer.Capture(this);
        GoTo(ValueAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            GoTo(ValueAt(e.GetPosition(this).X));
            e.Handled = true;
        }
    }

    private void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // User input, written as a current value: a two-way binding of the position stays. The value is kept within the
    // limits by RangeBase.
    internal void GoTo(double value)
    {
        SetCurrentValue(ValueProperty, value);
        InvalidateRender(false);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateRender(false);
    }

    private static void OnPeaksChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((Waveform)component).Tick();

    // Drawn again each frame while its parts are read.
    private void Tick()
    {
        if (_isTicking || !IsAttachedToVisualTree || Peaks is not { IsComplete: false })
        {
            return;
        }

        _isTicking = true;
        AnimationManager.AddTicker(_ =>
        {
            InvalidateRender(false);
            var goesOn = IsAttachedToVisualTree && Peaks is { IsComplete: false };
            _isTicking = goesOn;
            return !goesOn;
        });
    }
}
