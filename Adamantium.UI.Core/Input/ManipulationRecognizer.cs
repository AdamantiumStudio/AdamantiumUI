using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

internal sealed class ManipulationRecognizer
{
    private readonly HoldingRecognizer _holding;
    private readonly Action<Pointer, InputModifiers, uint> _leaveOver;
    private readonly List<Manipulation> _manipulations = new();

    public ManipulationRecognizer(HoldingRecognizer holding, Action<Pointer, InputModifiers, uint> leaveOver)
    {
        _holding = holding;
        _leaveOver = leaveOver;
    }

    public bool Join(Pointer pointer, IInputComponent hit, InputModifiers modifiers, uint timestamp)
    {
        if (pointer.Type != PointerType.Touch || TargetOf(hit) is not { } target)
        {
            return false;
        }

        var gesture = _manipulations.Find(m => m.Started && ReferenceEquals(m.Target, target));
        if (gesture == null)
        {
            return false;
        }

        gesture.Add(pointer);
        TakeOver(gesture, pointer, modifiers, timestamp);
        return true;
    }

    public void Down(Pointer pointer, IInputComponent hit, uint timestamp)
    {
        if (pointer.Type != PointerType.Touch || TargetOf(hit) is not { } target)
        {
            return;
        }

        var gesture = _manipulations.Find(m => ReferenceEquals(m.Target, target));
        if (gesture == null)
        {
            gesture = new Manipulation(target);
            _manipulations.Add(gesture);
        }

        gesture.Add(pointer);
        if (gesture.Fingers.Count < 2)
        {
            return;
        }

        foreach (var finger in gesture.Fingers.Where(finger => !finger.IsHolding))
        {
            _holding.Cancel(finger, timestamp);
        }
    }

    public void Move(Pointer pointer, InputModifiers modifiers, uint timestamp)
    {
        var gesture = Of(pointer);
        if (gesture == null || !gesture.Report(pointer))
        {
            return;
        }

        var step = gesture.Advance();
        if (!gesture.Started && (gesture.Fingers.Count < 2 || gesture.Blocked))
        {
            return;
        }

        gesture.Recognize();
        if (gesture.Recognized == ManipulationKinds.None ||
            !gesture.Started && (gesture.Recognized & Gestures.GetManipulationModes(gesture.Target)) == 0)
        {
            return;
        }

        if (!gesture.Started)
        {
            gesture.Started = true;
            foreach (var finger in gesture.Fingers)
            {
                TakeOver(gesture, finger, modifiers, timestamp);
            }

            Raise(gesture, ManipulationState.Started, Step.None, modifiers, timestamp);
        }

        Raise(gesture, ManipulationState.Delta, gesture.Filter(step), modifiers, timestamp);
    }

    public void Up(Pointer pointer, InputModifiers modifiers, uint timestamp)
    {
        var gesture = Of(pointer);
        if (gesture == null)
        {
            return;
        }

        gesture.Remove(pointer);
        if (gesture.Fingers.Count > 0)
        {
            return;
        }

        _manipulations.Remove(gesture);
        if (gesture.Started)
        {
            Raise(gesture, ManipulationState.Completed, Step.None, modifiers, timestamp);
        }
    }

    private Manipulation Of(Pointer pointer) => _manipulations.Find(m => m.Fingers.Contains(pointer));

    private static IInputComponent TargetOf(IInputComponent hit)
    {
        for (IUIComponent at = hit; at != null; at = at.VisualParent)
        {
            if (at is IInputComponent element && Gestures.GetIsManipulationEnabled(element))
            {
                return element;
            }
        }

        return null;
    }

    private void TakeOver(Manipulation gesture, Pointer finger, InputModifiers modifiers, uint timestamp)
    {
        _holding.Cancel(finger, timestamp);
        (finger.Captured ?? finger.DirectlyOver)?.RaiseEvent(
            new PointerEventArgs(finger, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerCanceledEvent });
        finger.IsManipulating = true;
        finger.Capture(gesture.Target);
        _leaveOver(finger, modifiers, timestamp);
    }

    private static void Raise(Manipulation gesture, ManipulationState state, Step step, InputModifiers modifiers,
        uint timestamp)
    {
        gesture.Total = gesture.Total.Then(step);
        gesture.Target.RaiseEvent(new ManipulationEventArgs(state, modifiers, timestamp)
        {
            RoutedEvent = Gestures.ManipulationEvent,
            Recognized = gesture.Recognized,
            Contacts = gesture.Fingers.Count,
            Origin = gesture.Origin,
            Translation = step.Translation,
            Scale = step.Scale,
            Rotation = step.Rotation,
            TotalTranslation = gesture.Total.Translation,
            TotalScale = gesture.Total.Scale,
            TotalRotation = gesture.Total.Rotation
        });
    }

    private readonly record struct Step(Vector2 Translation, double Scale, double Rotation)
    {
        public static Step None => new(Vector2.Zero, 1, 0);

        public Step Then(Step next) => new(Translation + next.Translation, Scale * next.Scale, Rotation + next.Rotation);
    }

    private sealed class Manipulation(IInputComponent target)
    {
        private readonly HashSet<Pointer> _reported = new();
        private readonly Dictionary<Pointer, PixelPoint> _landed = new();
        private Vector2 _middle;
        private double _spread;
        private double _angle;
        private Vector2 _panned;
        private double _spreadChanged;
        private double _turned;

        public IInputComponent Target { get; } = target;

        public List<Pointer> Fingers { get; } = new();

        public bool Started { get; set; }

        public ManipulationKinds Recognized { get; private set; }

        public Step Total { get; set; } = Step.None;

        public Vector2 Origin => _middle;

        public bool Blocked { get; private set; }

        public void Add(Pointer finger)
        {
            if (!Started && Fingers.Count > 0 && Gestures.GetStartsFromStillFingers(Target) &&
                Fingers.Any(moved => PlatformSettings.ExceedsDragThreshold(moved.ScreenPosition - _landed[moved])))
            {
                Blocked = true;
            }

            Fingers.Add(finger);
            _landed[finger] = finger.ScreenPosition;
            Mark();
            Rebase();
        }

        public void Remove(Pointer finger)
        {
            Fingers.Remove(finger);
            _landed.Remove(finger);
            finger.IsManipulating = false;
            finger.IsInGesture = false;
            if (Fingers.Count < 2)
            {
                Blocked = false;
            }

            Mark();
            if (Fingers.Count > 0)
            {
                Rebase();
            }
        }

        private void Mark()
        {
            var together = Started || Fingers.Count >= 2;
            foreach (var finger in Fingers)
            {
                finger.IsInGesture = together;
            }
        }

        public bool Report(Pointer finger)
        {
            if (_reported.Add(finger) && _reported.Count < Fingers.Count)
            {
                return false;
            }

            _reported.Clear();
            return true;
        }

        public Step Advance()
        {
            var (middle, spread, angle) = Measure();
            var pair = Fingers.Count >= 2;
            var step = new Step(middle - _middle,
                pair && _spread > 0 && spread > 0 ? spread / _spread : 1,
                pair ? Turn(angle - _angle) : 0);

            if (Started || pair)
            {
                _panned += step.Translation;
            }

            if (pair)
            {
                _spreadChanged += spread - _spread;
                _turned += step.Rotation;
            }

            (_middle, _spread, _angle) = (middle, spread, angle);
            return step;
        }

        public void Recognize()
        {
            if (PlatformSettings.ExceedsDragThreshold(_panned, Target))
            {
                Recognized |= ManipulationKinds.Pan;
            }

            if (Math.Abs(_spreadChanged) >= Gestures.PinchThreshold)
            {
                Recognized |= ManipulationKinds.Pinch;
            }

            if (Math.Abs(_turned) >= Gestures.RotationThreshold)
            {
                Recognized |= ManipulationKinds.Rotate;
            }
        }

        public Step Filter(Step step) => new(
            Recognized.HasFlag(ManipulationKinds.Pan) ? step.Translation : Vector2.Zero,
            Recognized.HasFlag(ManipulationKinds.Pinch) ? step.Scale : 1,
            Recognized.HasFlag(ManipulationKinds.Rotate) ? step.Rotation : 0);

        private void Rebase()
        {
            _reported.Clear();
            if (!Started)
            {
                (_panned, _spreadChanged, _turned) = (Vector2.Zero, 0, 0);
            }

            (_middle, _spread, _angle) = Measure();
        }

        private (Vector2 Middle, double Spread, double Angle) Measure()
        {
            var points = Fingers.Select(finger => finger.GetPosition(Target)).ToList();
            var middle = Vector2.Zero;
            foreach (var point in points)
            {
                middle += point;
            }

            middle /= points.Count;
            var spread = points.Average(point => (point - middle).Length());
            var angle = points.Count >= 2
                ? Math.Atan2(points[1].Y - points[0].Y, points[1].X - points[0].X) * 180 / Math.PI
                : 0;
            return (middle, spread, angle);
        }

        private static double Turn(double degrees) => degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;
    }
}
