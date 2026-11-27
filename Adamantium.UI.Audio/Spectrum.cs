using Adamantium.Pulsar;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Audio;

/// <summary>The spectrum of what an <see cref="AudioPlayback"/> plays, as bars from low to high that fall slowly, each with
/// its peak held over it a moment. It draws each frame while there is sound, and is still once it has fallen.</summary>
public class Spectrum : MeasurableUIComponent
{
    // Of the full height, a second.
    private const double FallRate = 1.6;
    private const double PeakFallRate = 0.5;
    private const double PeakHold = 0.6;

    private float[] _read = [];
    private double[] _levels = [];
    private double[] _peaks = [];
    private double[] _peakAges = [];
    private bool _isTicking;

    static Spectrum()
    {
        AddStyleSetToThemes<Themes.SpectrumStyleSet>();
    }

    public static readonly AdamantiumProperty PlaybackProperty = AdamantiumProperty.Register(nameof(Playback),
        typeof(AudioPlayback), typeof(Spectrum), new PropertyMetadata(null, OnPlaybackChanged));

    public static readonly AdamantiumProperty BandsProperty = AdamantiumProperty.Register(nameof(Bands),
        typeof(int), typeof(Spectrum), new PropertyMetadata(24, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BarBrushProperty = AdamantiumProperty.Register(nameof(BarBrush),
        typeof(Brush), typeof(Spectrum), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty PeakBrushProperty = AdamantiumProperty.Register(nameof(PeakBrush),
        typeof(Brush), typeof(Spectrum), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty BarSpacingProperty = AdamantiumProperty.Register(nameof(BarSpacing),
        typeof(double), typeof(Spectrum), new PropertyMetadata(2.0, PropertyMetadataOptions.AffectsRender));

    /// <summary>What is heard.</summary>
    public AudioPlayback Playback
    {
        get => GetValue<AudioPlayback>(PlaybackProperty);
        set => SetValue(PlaybackProperty, value);
    }

    /// <summary>How many bars, from 40 Hz to 16 kHz.</summary>
    public int Bands
    {
        get => GetValue<int>(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public Brush BarBrush
    {
        get => GetValue<Brush>(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>The line of each bar's peak; none where null.</summary>
    public Brush PeakBrush
    {
        get => GetValue<Brush>(PeakBrushProperty);
        set => SetValue(PeakBrushProperty, value);
    }

    public double BarSpacing
    {
        get => GetValue<double>(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    /// <summary>For tests: each bar's height, 0 to 1.</summary>
    internal IReadOnlyList<double> Levels => _levels;

    /// <summary>One frame of <paramref name="seconds"/>: the bars rise to what is heard, or fall; false once all are
    /// still at nothing and nothing plays.</summary>
    internal bool Advance(double seconds)
    {
        var bands = Math.Max(1, Bands);
        if (_levels.Length != bands)
        {
            _read = new float[bands];
            _levels = new double[bands];
            _peaks = new double[bands];
            _peakAges = new double[bands];
        }

        var playback = Playback;
        var isPlaying = playback is { State: AudioPlaybackState.Playing };
        if (isPlaying)
        {
            playback.ReadSpectrum(_read);
        }
        else
        {
            Array.Clear(_read);
        }

        var isStill = !isPlaying;
        for (var band = 0; band < bands; band++)
        {
            _levels[band] = Math.Max(_read[band], _levels[band] - FallRate * seconds);
            if (_levels[band] >= _peaks[band])
            {
                _peaks[band] = _levels[band];
                _peakAges[band] = 0;
            }
            else
            {
                _peakAges[band] += seconds;
                if (_peakAges[band] > PeakHold)
                {
                    _peaks[band] = Math.Max(_levels[band], _peaks[band] - PeakFallRate * seconds);
                }
            }

            isStill &= _peaks[band] <= 0;
        }

        InvalidateRender(false);
        return !isStill;
    }

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);
        var session = context.ForControl(this);
        var size = RenderSize;
        var bands = _levels.Length;
        if (session == null || bands == 0 || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var spacing = Math.Max(0, BarSpacing);
        var width = Math.Max(1, (size.Width - spacing * (bands - 1)) / bands);
        for (var band = 0; band < bands; band++)
        {
            var left = band * (width + spacing);
            var height = _levels[band] * size.Height;
            if (BarBrush != null && height > 0)
            {
                session.DrawRectangle(BarBrush, new Rect(left, size.Height - height, width, height));
            }

            if (PeakBrush != null && _peaks[band] > 0)
            {
                var top = Math.Min(size.Height - 2, (1 - _peaks[band]) * size.Height);
                session.DrawRectangle(PeakBrush, new Rect(left, top, width, 2));
            }
        }
    }

    // A picture of what is heard: the player answers for what plays.
    protected override AutomationPeer OnCreateAutomationPeer() => null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(null, Playback);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(Playback, null);
    }

    private static void OnPlaybackChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var spectrum = (Spectrum)component;
        if (spectrum.IsAttachedToVisualTree)
        {
            spectrum.Watch(e.OldValue as AudioPlayback, e.NewValue as AudioPlayback);
        }
    }

    private void Watch(AudioPlayback old, AudioPlayback now)
    {
        if (old != null)
        {
            old.StateChanged -= OnStateChanged;
        }

        if (now != null)
        {
            now.StateChanged += OnStateChanged;
        }

        Tick();
    }

    private void OnStateChanged(object sender, EventArgs e) => Tick();

    // On the heartbeat while there is sound, and while the bars fall after it; not while still.
    private void Tick()
    {
        if (_isTicking || !IsAttachedToVisualTree || Playback is not { State: AudioPlaybackState.Playing })
        {
            return;
        }

        _isTicking = true;
        AnimationManager.AddEndlessTicker(seconds =>
        {
            var goesOn = IsAttachedToVisualTree && Advance(seconds);
            _isTicking = goesOn;
            return !goesOn;
        });
    }
}
