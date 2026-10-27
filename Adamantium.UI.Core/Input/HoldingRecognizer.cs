using Adamantium.UI.Core.Dispatcher;

namespace Adamantium.UI.Core.Input;

/// <summary>Watches each finger and pen for a press held still, and raises <see cref="Gestures.HoldingEvent"/> on what
/// it pressed.</summary>
internal sealed class HoldingRecognizer
{
    private readonly Dictionary<uint, Hold> _holds = new();

    // A pen pressed with its side button is already the right button; only a plain contact long-presses.
    public void Down(Pointer pointer, IInputComponent target, MouseButtons button, InputModifiers modifiers, uint timestamp)
    {
        Finish(pointer.Id, HoldingState.Canceled, timestamp);
        if (pointer.Type == PointerType.Mouse || button != MouseButtons.Left)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = Gestures.HoldingTime, Tag = pointer.Id };
        timer.Tick += OnTick;
        _holds[pointer.Id] = new Hold(pointer, target, pointer.ScreenPosition, modifiers, timer);
        timer.Start();
    }

    // A contact that strays past the drag threshold is a drag, not a hold.
    public void Move(Pointer pointer, uint timestamp)
    {
        if (_holds.TryGetValue(pointer.Id, out var hold) &&
            PlatformSettings.ExceedsDragThreshold(pointer.ScreenPosition - hold.From))
        {
            Finish(pointer.Id, HoldingState.Canceled, timestamp);
        }
    }

    public void Up(Pointer pointer, uint timestamp) => Finish(pointer.Id, HoldingState.Completed, timestamp);

    public void Cancel(Pointer pointer, uint timestamp) => Finish(pointer.Id, HoldingState.Canceled, timestamp);

    /// <summary>Whether the contact <paramref name="id"/> is down and still waiting out the holding time.</summary>
    internal bool IsWaiting(uint id) => _holds.TryGetValue(id, out var hold) && !hold.Started;

    /// <summary>The contact has rested long enough: called by its timer on the loop - or, with no loop running, by
    /// whoever drains <see cref="LoopSignal"/>; never on the timer's own thread.</summary>
    internal void Elapsed(uint id, uint timestamp)
    {
        if (!_holds.TryGetValue(id, out var hold) || hold.Started)
        {
            return;
        }

        hold.Timer.Stop();
        hold.Started = true;
        hold.Pointer.IsHolding = true;

        // The capture stays where it is: a thumb or a selection held a moment before it moves goes on being dragged.
        // What would act on the lift - a button's click - asks IsHolding instead.
        Raise(hold, HoldingState.Started, timestamp);
    }

    private void OnTick(object sender, EventArgs e)
    {
        var timer = (DispatcherTimer)sender;
        var timestamp = (uint)Environment.TickCount;
        if (LoopSignal.IsClaimed || SynchronizationContext.Current != null)
        {
            ElapsedFor(timer, timestamp);
            return;
        }

        LoopSignal.Post(() => ElapsedFor(timer, timestamp));
    }

    private void ElapsedFor(DispatcherTimer timer, uint timestamp)
    {
        var id = (uint)timer.Tag;
        if (_holds.TryGetValue(id, out var hold) && hold.Timer == timer)
        {
            Elapsed(id, timestamp);
        }
    }

    private void Finish(uint id, HoldingState state, uint timestamp)
    {
        if (!_holds.Remove(id, out var hold))
        {
            return;
        }

        Stop(hold);
        if (hold.Started)
        {
            Raise(hold, state, timestamp);
        }
    }

    private static void Stop(Hold hold)
    {
        hold.Timer.Stop();
        hold.Timer.Dispose();
        hold.Pointer.IsHolding = false;
    }

    private static void Raise(Hold hold, HoldingState state, uint timestamp) =>
        hold.Target.RaiseEvent(new HoldingEventArgs(hold.Pointer, state, hold.Modifiers, timestamp)
        {
            RoutedEvent = Gestures.HoldingEvent
        });

    private sealed class Hold(Pointer pointer, IInputComponent target, PixelPoint from, InputModifiers modifiers,
        DispatcherTimer timer)
    {
        public Pointer Pointer { get; } = pointer;

        public IInputComponent Target { get; } = target;

        public PixelPoint From { get; } = from;

        public InputModifiers Modifiers { get; } = modifiers;

        public DispatcherTimer Timer { get; } = timer;

        public bool Started { get; set; }
    }
}
