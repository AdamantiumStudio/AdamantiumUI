using Adamantium.Pulsar;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Audio;

/// <summary>A sound's waveform: of each column, the least and the greatest sample of all channels; what is played of it
/// in <see cref="PlayedBrush"/>. Drawn as it is read, a part at a time. It takes no input: the position slider over it
/// does.</summary>
public class Waveform : MeasurableUIComponent
{
    private bool _isTicking;

    static Waveform()
    {
        AddStyleSetToThemes<Themes.WaveformStyleSet>();
    }

    public static readonly AdamantiumProperty PeaksProperty = AdamantiumProperty.Register(nameof(Peaks),
        typeof(SoundWaveform), typeof(Waveform),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnPeaksChanged));

    public static readonly AdamantiumProperty PositionProperty = AdamantiumProperty.Register(nameof(Position),
        typeof(TimeSpan), typeof(Waveform), new PropertyMetadata(TimeSpan.Zero, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.Register(nameof(Duration),
        typeof(TimeSpan), typeof(Waveform), new PropertyMetadata(TimeSpan.Zero, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty WaveBrushProperty = AdamantiumProperty.Register(nameof(WaveBrush),
        typeof(Brush), typeof(Waveform), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty PlayedBrushProperty = AdamantiumProperty.Register(nameof(PlayedBrush),
        typeof(Brush), typeof(Waveform), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public SoundWaveform Peaks
    {
        get => GetValue<SoundWaveform>(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public TimeSpan Position
    {
        get => GetValue<TimeSpan>(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue<TimeSpan>(DurationProperty);
        set => SetValue(DurationProperty, value);
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

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);
        var session = context.ForControl(this);
        var size = RenderSize;
        if (session == null || Peaks is not { } peaks || size.Width < 1 || size.Height <= 0)
        {
            return;
        }

        var columns = (int)size.Width;
        var played = Duration > TimeSpan.Zero ? Position.TotalSeconds / Duration.TotalSeconds * columns : 0;
        var middle = size.Height / 2;
        var read = peaks.PartsRead;
        for (var column = 0; column < columns; column++)
        {
            var from = (int)((long)column * peaks.Parts / columns);
            var to = Math.Max(from + 1, (int)((long)(column + 1) * peaks.Parts / columns));
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

            var brush = column < played ? PlayedBrush ?? WaveBrush : WaveBrush;
            // A column of silence is still a line, so the wave reads as one.
            var top = middle - Math.Min(1, greatest) * middle;
            var height = Math.Max(1, (Math.Min(1, greatest) - Math.Max(-1, least)) * middle);
            if (brush != null)
            {
                session.DrawRectangle(brush, new Rect(column, top, 1, height));
            }
        }
    }

    // A picture under the position slider, which answers for the position.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
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
