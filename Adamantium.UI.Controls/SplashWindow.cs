using System;
using System.Diagnostics;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>The window an application shows while it starts: shown at once, on a thread and a connection to the desktop
/// of its own, before the themes, the services and the main window are made, and closed when the main window has drawn.
/// Closing it before then cancels the start. It wears a theme of its own, never the application's, which is not made
/// yet when it appears.</summary>
public class SplashWindow : Window
{
    public static readonly AdamantiumProperty ShowCloseButtonProperty = AdamantiumProperty.Register(nameof(ShowCloseButton),
        typeof(bool), typeof(SplashWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ElapsedProperty = AdamantiumProperty.Register(nameof(Elapsed),
        typeof(TimeSpan), typeof(SplashWindow), new PropertyMetadata(TimeSpan.Zero));

    private static readonly TimeSpan ElapsedStep = TimeSpan.FromMilliseconds(100);

    private readonly long _shownAt = Stopwatch.GetTimestamp();
    private DispatcherTimer _clock;
    private ButtonBase _closeButton;

    static SplashWindow()
    {
        ResizeModeProperty.OverrideMetadata(typeof(SplashWindow), new PropertyMetadata(WindowResizeMode.NoResize));
        RemembersPlacementProperty.OverrideMetadata(typeof(SplashWindow), new PropertyMetadata(false));
        StartupLocationProperty.OverrideMetadata(typeof(SplashWindow), new PropertyMetadata(WindowStartupLocation.CenterScreen));
        ActivateOnShowProperty.OverrideMetadata(typeof(SplashWindow), new PropertyMetadata(false));
    }

    /// <summary>Whether the template shows the button that closes the window - which, before the hand-over, cancels the
    /// start.</summary>
    public bool ShowCloseButton
    {
        get => GetValue<bool>(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>How long the window has been up, in tenths of a second: what a ticking timer binds to.</summary>
    public TimeSpan Elapsed => GetValue<TimeSpan>(ElapsedProperty);

    public SplashWindow()
    {
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_clock != null)
        {
            return;
        }

        _clock = new DispatcherTimer { Interval = ElapsedStep };
        _clock.Tick += OnClockTick;
        _clock.Start();
    }

    private void OnClosed(object sender, EventArgs e) => _clock?.Dispose();

    private void OnClockTick(object sender, EventArgs e)
    {
        var elapsed = Stopwatch.GetElapsedTime(_shownAt);
        SetCurrentValue(ElapsedProperty, TimeSpan.FromTicks(elapsed.Ticks - elapsed.Ticks % ElapsedStep.Ticks));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();
        _closeButton = GetTemplateChild("PART_CloseButton") as ButtonBase;
        if (_closeButton != null)
        {
            _closeButton.Click += OnCloseClick;
        }
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
    }

    private void DetachParts()
    {
        if (_closeButton != null)
        {
            _closeButton.Click -= OnCloseClick;
            _closeButton = null;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
