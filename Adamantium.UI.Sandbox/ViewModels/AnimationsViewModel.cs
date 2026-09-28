using System;
using System.Collections.ObjectModel;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Animations tab: a bench measuring what running animations cost per frame, and whether the cost follows their
/// count or what they repaint.</summary>
[ViewModel]
public partial class AnimationsViewModel : TabPageViewModel
{
    public AnimationsViewModel() : base("Animations") { }

    /// <summary>One entry per running ring indicator (a transform animation); each indicator class has its own collection,
    /// since cost follows what is animated.</summary>
    public ObservableCollection<int> Spinners { get; } = new();

    // One collection per indicator CLASS - every kind the theme ships, so the bench covers the whole zoo rather than the
    // two or three that happened to be handy. Classes is not a bindable string, hence a collection each.
    public ObservableCollection<int> DotsIndicators { get; } = new();

    public ObservableCollection<int> RippleIndicators { get; } = new();

    public ObservableCollection<int> ArcIndicators { get; } = new();

    public ObservableCollection<int> BarIndicators { get; } = new();

    public ObservableCollection<int> AntsIndicators { get; } = new();

    public ObservableCollection<int> EqualizerIndicators { get; } = new();

    public ObservableCollection<int> FlipIndicators { get; } = new();

    public ObservableCollection<int> HeartbeatIndicators { get; } = new();

    public ObservableCollection<int> ShimmerIndicators { get; } = new();

    [Command] private void AddDots() => Fill(DotsIndicators);

    [Command] private void AddRipple() => Fill(RippleIndicators);

    [Command] private void AddArc() => Fill(ArcIndicators);

    [Command] private void AddBar() => Fill(BarIndicators);

    [Command] private void AddAnts() => Fill(AntsIndicators);

    [Command] private void AddEqualizer() => Fill(EqualizerIndicators);

    [Command] private void AddFlip() => Fill(FlipIndicators);

    [Command] private void AddHeartbeat() => Fill(HeartbeatIndicators);

    [Command] private void AddShimmer() => Fill(ShimmerIndicators);

    /// <summary>Everything at once - the state the tab is really for: enough load that a cost shows up at all.</summary>
    [Command]
    private void AddEveryKind()
    {
        foreach (var kind in AllKinds) Fill(kind);
    }

    [Command]
    private void ClearKinds()
    {
        foreach (var kind in AllKinds) kind.Clear();
        Spinners.Clear();
    }

    private ObservableCollection<int>[] AllKinds =>
    [
        DotsIndicators, RippleIndicators, ArcIndicators, BarIndicators, AntsIndicators,
        EqualizerIndicators, FlipIndicators, HeartbeatIndicators, ShimmerIndicators
    ];

    private static void Fill(ObservableCollection<int> target)
    {
        for (var i = 0; i < 25; i++) target.Add(target.Count);
    }

    /// <summary>Every entry is the SAME brush instance, so a card's template binds straight to its own item and never
    /// has to reach back up the tree. The collection is therefore both the painter count and the brush.</summary>
    public ObservableCollection<SolidColorBrush> Skeletons { get; } = new();

    [Command] private void AddOne() => AddSpinners(1);

    [Command] private void AddFive() => AddSpinners(5);

    [Command] private void AddTwentyFive() => AddSpinners(25);

    [Command] private void ClearSpinners() => Spinners.Clear();

    [Command] private void AddTenSkeletons() => AddSkeletons(10);

    [Command] private void AddHundredSkeletons() => AddSkeletons(100);

    [Command] private void ClearSkeletons() => Skeletons.Clear();

    private void AddSpinners(int count)
    {
        for (var i = 0; i < count; i++) Spinners.Add(Spinners.Count);
    }

    private void AddSkeletons(int count)
    {
        for (var i = 0; i < count; i++) Skeletons.Add(SharedPulse);
    }

    /// <summary>ONE brush, painted by every card below and driven by ONE animation - the other half of the comparison.
    /// This is how the real loading skeletons work (the theme's single <c>SkeletonPulseFill</c>), and it is what tells
    /// the two costs apart: if 100 cards on one animated brush cost like 100 animations, the price is being paid per
    /// PAINTER, not per animation.</summary>
    public SolidColorBrush SharedPulse { get; } = new() { Color = Colors.White, Opacity = 0.05 };

    [Bindable] private bool _sharedPulseRunning;

    [Command]
    private void ToggleSharedPulse()
    {
        if (SharedPulseRunning)
        {
            AnimationManager.Cancel(SharedPulse);
            SharedPulseRunning = false;
            return;
        }

        var pulse = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.2),
            IterationCount = double.PositiveInfinity,
            AutoReverse = true,
            Easing = new LinearEasing()
        };
        pulse.KeyFrames.Add(OpacityAt(0.0, 0.05));
        pulse.KeyFrames.Add(OpacityAt(1.0, 0.20));
        pulse.Apply(SharedPulse);
        SharedPulseRunning = true;
    }

    private static KeyFrame OpacityAt(double cue, double opacity)
    {
        var frame = new KeyFrame { Cue = cue };
        frame.Setters.Add(new Setter("Opacity", opacity));
        return frame;
    }

    private static void Add(ObservableCollection<int> target, int count)
    {
        for (var i = 0; i < count; i++)
        {
            target.Add(target.Count);
        }
    }
}
