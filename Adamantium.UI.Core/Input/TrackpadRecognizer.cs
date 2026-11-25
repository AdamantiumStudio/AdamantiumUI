using Adamantium.Mathematics;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Core.Input;

internal sealed class TrackpadRecognizer
{
    private const int Fingers = 2;

    private IInputComponent _target;
    private ManipulationKinds _active;
    private ManipulationKinds _recognized;

    // Not left to the wheel until it ends, though its element has gone meanwhile.
    private ManipulationKinds _taken;

    // Going on over an element it may not start a gesture of: it joins one that another kind starts.
    private ManipulationKinds _waiting;
    private IInputComponent _waitingFor;
    private Vector2<double> _translation;
    private double _scale;
    private double _rotation;
    private bool _panTaken;

    public bool Take(RawTrackpadEventArgs e, Func<IInputComponent> hit)
    {
        if (e.Type == RawTrackpadEventType.Inertia)
        {
            return e.Kind == ManipulationKinds.Pan && _panTaken;
        }

        ForgetReleased();
        if (e.Type == RawTrackpadEventType.Started)
        {
            Start(e, hit());
        }

        var taken = (_taken & e.Kind) != 0;
        var active = (_active & e.Kind) != 0;
        if (e.Type == RawTrackpadEventType.Completed)
        {
            _taken &= ~e.Kind;
            _waiting &= ~e.Kind;
            if (active)
            {
                End(e);
            }
        }
        else if (active)
        {
            Move(e);
        }

        return taken;
    }

    public void ForgetReleased()
    {
        if (_target is { IsAttachedToVisualTree: false })
        {
            Forget();
        }

        if (_waitingFor is { IsAttachedToVisualTree: false })
        {
            _waiting = ManipulationKinds.None;
            _waitingFor = null;
        }
    }

    private void Start(RawTrackpadEventArgs e, IInputComponent hit)
    {
        // Its end was not told of - a modal window took the rest of it, say.
        if ((_active & e.Kind) != 0)
        {
            End(e);
        }

        _taken &= ~e.Kind;
        _waiting &= ~e.Kind;
        if (e.Kind == ManipulationKinds.Pan)
        {
            _panTaken = false;
        }

        var target = ManipulationRecognizer.TargetOf(hit);
        if (target == null || e.Kind == ManipulationKinds.Pan && !TakesPan(target, hit, e))
        {
            return;
        }

        // The other kinds go on over an element the fingers have left: theirs is another gesture.
        if (_target != null && !ReferenceEquals(_target, target))
        {
            _active = ManipulationKinds.None;
            Raise(ManipulationState.Completed, e);
            Forget();
        }

        // A pan is the target's by its own word: what its modes keep for drawing is fingers on a screen.
        if (_target == null && e.Kind != ManipulationKinds.Pan && (Gestures.GetManipulationModes(target) & e.Kind) == 0)
        {
            _waiting = ReferenceEquals(_waitingFor, target) ? _waiting | e.Kind : e.Kind;
            _waitingFor = target;
            return;
        }

        var kinds = e.Kind;
        if (ReferenceEquals(_waitingFor, target))
        {
            kinds |= _waiting;
            _waiting = ManipulationKinds.None;
            _waitingFor = null;
        }

        _taken |= kinds;
        _active |= kinds;
        _recognized |= kinds;
        _panTaken |= (kinds & ManipulationKinds.Pan) != 0;
        if (_target != null)
        {
            return;
        }

        _target = target;
        (_translation, _scale, _rotation) = (Vector2<double>.Zero, 1, 0);
        Raise(ManipulationState.Started, e);
    }

    // Over what it holds - a list in a 3D view - a pan scrolls. With Control it zooms, as the wheel does: for a mouse that
    // has no second finger to pinch with.
    private static bool TakesPan(IInputComponent target, IInputComponent hit, RawTrackpadEventArgs e) =>
        (e.InputModifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) == 0 &&
        (target is ITrackpadPanTarget judge
            ? judge.TakesTrackpadPan(hit)
            : ReferenceEquals(hit, target) && Gestures.GetIsTrackpadPanEnabled(target));

    private void Move(RawTrackpadEventArgs e)
    {
        if (e.Translation == Vector2<double>.Zero && e.Scale == 1 && e.Rotation == 0)
        {
            return;
        }

        // In the target's units, which a zoom around it makes others than the root's.
        var translation = Pointer.ToLocal(_target, e.Position + e.Translation) - Pointer.ToLocal(_target, e.Position);
        _translation += translation;
        _scale *= e.Scale;
        _rotation += e.Rotation;
        Raise(ManipulationState.Delta, e, translation);
    }

    private void End(RawTrackpadEventArgs e)
    {
        _active &= ~e.Kind;
        if (_active != ManipulationKinds.None)
        {
            return;
        }

        Raise(ManipulationState.Completed, e);
        Forget();
    }

    private void Forget()
    {
        _target = null;
        _active = ManipulationKinds.None;
        _recognized = ManipulationKinds.None;
    }

    private void Raise(ManipulationState state, RawTrackpadEventArgs e, Vector2<double> translation = default)
    {
        var moves = state == ManipulationState.Delta;
        _target.RaiseEvent(new ManipulationEventArgs(state, e.InputModifiers, e.Timestamp)
        {
            RoutedEvent = Gestures.ManipulationEvent,
            Recognized = _recognized,
            Contacts = Fingers,
            IsTrackpad = true,
            Origin = Pointer.ToLocal(_target, e.Position),
            Translation = translation,
            Scale = moves ? e.Scale : 1,
            Rotation = moves ? e.Rotation : 0,
            TotalTranslation = _translation,
            TotalScale = _scale,
            TotalRotation = _rotation
        });
    }
}
