using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Fingers on a trackpad: a pinch and a rotation go to the element that takes gestures, a pan to one that asks
/// for it, and what nobody takes is left to the wheel.</summary>
[TestFixture]
public class TrackpadGestureTests
{
    private static readonly Vector2<double> Middle = new(500, 300);

    private Window _window;
    private Border _pad;
    private List<ManipulationEventArgs> _events;

    [SetUp]
    public void SetUp()
    {
        _pad = new Border { Background = Brushes.Gray };
        _events = [];
        _pad.AddHandler(Gestures.ManipulationEvent, new ManipulationEventHandler((_, e) => _events.Add(e)));
        _window = new Window { Width = 1000, Height = 600, Content = _pad };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var kind in new[] { ManipulationKinds.Pan, ManipulationKinds.Pinch, ManipulationKinds.Rotate })
        {
            Send(RawTrackpadEventType.Completed, kind);
        }
    }

    [Test]
    public void APan_IsLeftToTheWheel_UnlessTheElementAsksForIt()
    {
        Gestures.SetIsManipulationEnabled(_pad, true);

        Assert.Multiple(() =>
        {
            Assert.That(Send(RawTrackpadEventType.Started, ManipulationKinds.Pan), Is.False);
            Assert.That(Send(RawTrackpadEventType.Delta, ManipulationKinds.Pan, new Vector2<double>(5, 0)), Is.False);
            Assert.That(Send(RawTrackpadEventType.Completed, ManipulationKinds.Pan), Is.False);
            Assert.That(Send(RawTrackpadEventType.Inertia, ManipulationKinds.Pan, new Vector2<double>(5, 0)), Is.False);
            Assert.That(_events, Is.Empty);
        });
    }

    [Test]
    public void APan_GoesToTheElementThatAsksForIt_AndItsInertiaNowhere()
    {
        TakePans();

        Send(RawTrackpadEventType.Started, ManipulationKinds.Pan, new Vector2<double>(1, 2));
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Pan, new Vector2<double>(10, -4));
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Pan);
        var inertia = Send(RawTrackpadEventType.Inertia, ManipulationKinds.Pan, new Vector2<double>(50, 50));

        Assert.Multiple(() =>
        {
            Assert.That(inertia, Is.True, "it turns no wheel either");
            Assert.That(_events.Select(e => e.State), Is.EqualTo(new[]
            {
                ManipulationState.Started, ManipulationState.Delta, ManipulationState.Delta, ManipulationState.Completed
            }));
            Assert.That(_events[2].Translation, Is.EqualTo(new Vector2<double>(10, -4)));
            Assert.That(_events[3].TotalTranslation, Is.EqualTo(new Vector2<double>(11, -2)));
            Assert.That(_events.All(e => e.Recognized == ManipulationKinds.Pan), Is.True);
        });
    }

    [Test]
    public void APanWithControl_IsLeftToTheWheel_ThatZooms()
    {
        TakePans();

        Assert.That(Send(RawTrackpadEventType.Started, ManipulationKinds.Pan, modifiers: InputModifiers.LeftControl), Is.False);
    }

    [Test]
    public void APinch_GoesToAnElementThatTakesGestures()
    {
        Gestures.SetIsManipulationEnabled(_pad, true);

        Send(RawTrackpadEventType.Started, ManipulationKinds.Pinch);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Pinch, scale: 1.5);
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Pinch);

        Assert.Multiple(() =>
        {
            Assert.That(_events.Select(e => e.State), Is.EqualTo(new[]
            {
                ManipulationState.Started, ManipulationState.Delta, ManipulationState.Completed
            }));
            Assert.That(_events[1].Scale, Is.EqualTo(1.5));
            Assert.That(_events[1].Origin, Is.EqualTo(Middle), "about the pointer");
        });
    }

    [Test]
    public void APinch_OverWhatTakesNoGestures_IsLeftToTheWheel()
    {
        Assert.That(Send(RawTrackpadEventType.Started, ManipulationKinds.Pinch), Is.False);
    }

    [Test]
    public void APinchAndARotationTogether_AreOneGesture()
    {
        Gestures.SetIsManipulationEnabled(_pad, true);

        Send(RawTrackpadEventType.Started, ManipulationKinds.Pinch);
        Send(RawTrackpadEventType.Started, ManipulationKinds.Rotate);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Rotate, rotation: 12);
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Pinch);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Rotate, rotation: 3);
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Rotate);

        Assert.Multiple(() =>
        {
            Assert.That(_events.Count(e => e.State == ManipulationState.Started), Is.EqualTo(1));
            Assert.That(_events.Count(e => e.State == ManipulationState.Completed), Is.EqualTo(1));
            Assert.That(_events[^1].TotalRotation, Is.EqualTo(15));
            Assert.That(_events[^1].Recognized, Is.EqualTo(ManipulationKinds.Pinch | ManipulationKinds.Rotate));
        });
    }

    [Test]
    public void AnElementTakenOutUnderTheFingers_IsToldNothingMore()
    {
        TakePans();
        Send(RawTrackpadEventType.Started, ManipulationKinds.Pan);
        _window.Content = null;
        WindowExtension.UpdateTree(_window);
        _events.Clear();

        Send(RawTrackpadEventType.Delta, ManipulationKinds.Pan, new Vector2<double>(5, 5));

        Assert.That(_events, Is.Empty);
    }

    [Test]
    public void AnElementTakenOutUnderTheFingers_LeavesNothingOfTheGestureToTheWheel()
    {
        TakePans();
        Send(RawTrackpadEventType.Started, ManipulationKinds.Pan);
        _window.Content = null;
        WindowExtension.UpdateTree(_window);

        Assert.Multiple(() =>
        {
            Assert.That(Send(RawTrackpadEventType.Delta, ManipulationKinds.Pan, new Vector2<double>(5, 5)), Is.True);
            Assert.That(Send(RawTrackpadEventType.Completed, ManipulationKinds.Pan), Is.True);
        });
    }

    [Test]
    public void APanOverWhatTheElementHolds_IsLeftToTheWheel()
    {
        TakePans();
        _pad.Child = new Border { Background = Brushes.Red };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.That(Send(RawTrackpadEventType.Started, ManipulationKinds.Pan), Is.False);
    }

    [Test]
    public void AKindThatMayNotStartAGesture_JoinsOneAnotherStarts()
    {
        Gestures.SetIsManipulationEnabled(_pad, true);
        Gestures.SetManipulationModes(_pad, ManipulationKinds.Pinch);

        Send(RawTrackpadEventType.Started, ManipulationKinds.Rotate);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Rotate, rotation: 5);
        Assert.That(_events, Is.Empty);
        Send(RawTrackpadEventType.Started, ManipulationKinds.Pinch);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Rotate, rotation: 7);

        Assert.That(_events[^1].Rotation, Is.EqualTo(7));
    }

    private static readonly Dictionary<ushort, char> German = new()
    {
        [0x06] = 'y', [0x10] = 'z', [0x21] = 'ü', [0x1E] = '+', [0x18] = '\0', [0x1B] = 'ß', [0x2C] = '-', [0x29] = 'ö',
        [0x27] = 'ä', [0x2A] = '#', [0x32] = '^'
    };

    private static readonly Dictionary<ushort, char> French = new()
    {
        [0x0C] = 'a', [0x00] = 'q', [0x0D] = 'z', [0x06] = 'w', [0x29] = 'm', [0x2E] = ',', [0x2B] = ';', [0x2F] = ':',
        [0x2C] = '=', [0x1B] = ')', [0x18] = '-', [0x12] = '&'
    };

    [Test]
    public void TheKeysOnMacOS_AreThoseTheLayoutNames()
    {
        var german = MacOSKeys.InLayout(code => In(German, code));
        var french = MacOSKeys.InLayout(code => In(French, code));
        var none = MacOSKeys.InLayout(_ => '\0');

        Assert.Multiple(() =>
        {
            Assert.That(german[0x06], Is.EqualTo(Key.Y));
            Assert.That(german[0x1E], Is.EqualTo(Key.OemPlus), "the plus key");
            Assert.That(german[0x2C], Is.EqualTo(Key.OemMinus), "the minus key");
            Assert.That(german[0x21], Is.EqualTo(Key.OemOpenBrackets), "a letter with no key, by position");
            Assert.That(french[0x0C], Is.EqualTo(Key.A));
            Assert.That(french[0x29], Is.EqualTo(Key.M));
            Assert.That(french.ContainsKey(0x12), Is.False, "the digits' row goes by position");
            Assert.That(none[0x00], Is.EqualTo(Key.A), "no layout: by position");
            foreach (var layout in new[] { german, french, none })
            {
                Assert.That(layout.Values, Is.Unique);
                Assert.That(layout, Has.Count.EqualTo(37), "every letter and sign is somewhere");
            }
        });
    }

    // A layout that differs from the American one only in what it lists.
    private static char In(Dictionary<ushort, char> layout, ushort code) => layout.TryGetValue(code, out var character)
        ? character
        : MacOSKeys.ToKey(code) is >= Key.A and <= Key.Z and var key ? (char)('a' + (key - Key.A)) : '\0';

    private void TakePans()
    {
        Gestures.SetIsManipulationEnabled(_pad, true);
        Gestures.SetIsTrackpadPanEnabled(_pad, true);
    }

    private bool Send(RawTrackpadEventType type, ManipulationKinds kind, Vector2<double> translation = default,
        double scale = 1, double rotation = 0, InputModifiers modifiers = InputModifiers.None) =>
        PointerDevice.Current.ProcessTrackpad(new RawTrackpadEventArgs(type, kind, _window, Middle, modifiers, 0)
        {
            Translation = translation, Scale = scale, Rotation = rotation
        });
}
