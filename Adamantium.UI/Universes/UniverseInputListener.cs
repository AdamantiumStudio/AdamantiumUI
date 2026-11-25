using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using UniverseMouseButton = Adamantium.Multiverse.Input.MouseButton;

namespace Adamantium.UI.Universes;

/// <summary>Listens to the keys, text, mouse and gestures of the element a universe is shown in, translates them to the
/// engine's input and gives them to its <see cref="IUniverseInputTarget"/> - a universe in this process or in another.
/// </summary>
internal sealed class UniverseInputListener : IDisposable
{
    private static readonly HashSet<Keys> KnownKeys = [.. Enum.GetValues<Keys>()];

    internal static readonly Dictionary<Key, Keys> TranslationKeys;
    internal static readonly Dictionary<MouseButtons, UniverseMouseButton> MouseTranslationKeys;

    private readonly IInputComponent _component;
    private readonly IUniverseInputTarget _target;

    static UniverseInputListener()
    {
        TranslationKeys = new Dictionary<Key, Keys>();
        TranslationKeys[Key.None] = Keys.None;
        TranslationKeys[Key.BackSpace] = Keys.Back;
        TranslationKeys[Key.Tab] = Keys.Tab;
        TranslationKeys[Key.Enter] = Keys.Enter;
        TranslationKeys[Key.Pause] = Keys.Pause;
        TranslationKeys[Key.CapsLock] = Keys.CapsLock;
        TranslationKeys[Key.IMEKana] = Keys.Kana;
        TranslationKeys[Key.IMEKanji] = Keys.Kanji;
        TranslationKeys[Key.Escape] = Keys.Escape;
        TranslationKeys[Key.IMEConvert] = Keys.ImeConvert;
        TranslationKeys[Key.IMENonconvert] = Keys.ImeNonConvert;
        TranslationKeys[Key.Space] = Keys.Space;
        TranslationKeys[Key.PageUp] = Keys.PageUp;
        TranslationKeys[Key.PageDown] = Keys.PageDown;
        TranslationKeys[Key.End] = Keys.End;
        TranslationKeys[Key.Home] = Keys.Home;
        TranslationKeys[Key.LeftArrow] = Keys.LeftArrow;
        TranslationKeys[Key.UpArrow] = Keys.UpArrow;
        TranslationKeys[Key.RightArrow] = Keys.RightArrow;
        TranslationKeys[Key.DownArrow] = Keys.DownArrow;
        TranslationKeys[Key.Select] = Keys.Select;
        TranslationKeys[Key.Execute] = Keys.Execute;
        TranslationKeys[Key.PrintScreen] = Keys.PrintScreen;
        TranslationKeys[Key.Insert] = Keys.Insert;
        TranslationKeys[Key.Delete] = Keys.Delete;
        TranslationKeys[Key.Help] = Keys.Help;
        TranslationKeys[Key.D0] = Keys.Digit0;
        TranslationKeys[Key.D1] = Keys.Digit1;
        TranslationKeys[Key.D2] = Keys.Digit2;
        TranslationKeys[Key.D3] = Keys.Digit3;
        TranslationKeys[Key.D4] = Keys.Digit4;
        TranslationKeys[Key.D5] = Keys.Digit5;
        TranslationKeys[Key.D6] = Keys.Digit6;
        TranslationKeys[Key.D7] = Keys.Digit7;
        TranslationKeys[Key.D8] = Keys.Digit8;
        TranslationKeys[Key.D9] = Keys.Digit9;
        TranslationKeys[Key.A] = Keys.A;
        TranslationKeys[Key.B] = Keys.B;
        TranslationKeys[Key.C] = Keys.C;
        TranslationKeys[Key.D] = Keys.D;
        TranslationKeys[Key.E] = Keys.E;
        TranslationKeys[Key.F] = Keys.F;
        TranslationKeys[Key.G] = Keys.G;
        TranslationKeys[Key.H] = Keys.H;
        TranslationKeys[Key.I] = Keys.I;
        TranslationKeys[Key.J] = Keys.J;
        TranslationKeys[Key.K] = Keys.K;
        TranslationKeys[Key.L] = Keys.L;
        TranslationKeys[Key.M] = Keys.M;
        TranslationKeys[Key.N] = Keys.N;
        TranslationKeys[Key.O] = Keys.O;
        TranslationKeys[Key.P] = Keys.P;
        TranslationKeys[Key.Q] = Keys.Q;
        TranslationKeys[Key.R] = Keys.R;
        TranslationKeys[Key.S] = Keys.S;
        TranslationKeys[Key.T] = Keys.T;
        TranslationKeys[Key.U] = Keys.U;
        TranslationKeys[Key.V] = Keys.V;
        TranslationKeys[Key.W] = Keys.W;
        TranslationKeys[Key.X] = Keys.X;
        TranslationKeys[Key.Y] = Keys.Y;
        TranslationKeys[Key.Z] = Keys.Z;
        TranslationKeys[Key.LeftWin] = Keys.LeftWindows;
        TranslationKeys[Key.RightWin] = Keys.RightWindows;
        TranslationKeys[Key.Apps] = Keys.Apps;
        TranslationKeys[Key.Sleep] = Keys.Sleep;
        TranslationKeys[Key.NumPad0] = Keys.NumPad0;
        TranslationKeys[Key.NumPad1] = Keys.NumPad1;
        TranslationKeys[Key.NumPad2] = Keys.NumPad2;
        TranslationKeys[Key.NumPad3] = Keys.NumPad3;
        TranslationKeys[Key.NumPad4] = Keys.NumPad4;
        TranslationKeys[Key.NumPad5] = Keys.NumPad5;
        TranslationKeys[Key.NumPad6] = Keys.NumPad6;
        TranslationKeys[Key.NumPad7] = Keys.NumPad7;
        TranslationKeys[Key.NumPad8] = Keys.NumPad8;
        TranslationKeys[Key.NumPad9] = Keys.NumPad9;
        TranslationKeys[Key.NumPadMultiply] = Keys.Multiply;
        TranslationKeys[Key.NumPadAdd] = Keys.Add;
        TranslationKeys[Key.Separator] = Keys.Separator;
        TranslationKeys[Key.NumPadSubtract] = Keys.Subtract;
        TranslationKeys[Key.NumPadDecimal] = Keys.Decimal;
        TranslationKeys[Key.NumPadDivide] = Keys.Divide;
        TranslationKeys[Key.F1] = Keys.F1;
        TranslationKeys[Key.F2] = Keys.F2;
        TranslationKeys[Key.F3] = Keys.F3;
        TranslationKeys[Key.F4] = Keys.F4;
        TranslationKeys[Key.F5] = Keys.F5;
        TranslationKeys[Key.F6] = Keys.F6;
        TranslationKeys[Key.F7] = Keys.F7;
        TranslationKeys[Key.F8] = Keys.F8;
        TranslationKeys[Key.F9] = Keys.F9;
        TranslationKeys[Key.F10] = Keys.F10;
        TranslationKeys[Key.F11] = Keys.F11;
        TranslationKeys[Key.F12] = Keys.F12;
        TranslationKeys[Key.F13] = Keys.F13;
        TranslationKeys[Key.F14] = Keys.F14;
        TranslationKeys[Key.F15] = Keys.F15;
        TranslationKeys[Key.F16] = Keys.F16;
        TranslationKeys[Key.F17] = Keys.F17;
        TranslationKeys[Key.F18] = Keys.F18;
        TranslationKeys[Key.F19] = Keys.F19;
        TranslationKeys[Key.F20] = Keys.F20;
        TranslationKeys[Key.F21] = Keys.F21;
        TranslationKeys[Key.F22] = Keys.F22;
        TranslationKeys[Key.F23] = Keys.F23;
        TranslationKeys[Key.F24] = Keys.F24;
        TranslationKeys[Key.NumLock] = Keys.NumLock;
        TranslationKeys[Key.ScrollLock] = Keys.ScrollLock;
        TranslationKeys[Key.LeftShift] = Keys.LeftShift;
        TranslationKeys[Key.RightShift] = Keys.RightShift;
        TranslationKeys[Key.LeftCtrl] = Keys.LeftControl;
        TranslationKeys[Key.RightCtrl] = Keys.RightControl;
        TranslationKeys[Key.LeftAlt] = Keys.LeftAlt;
        TranslationKeys[Key.RightAlt] = Keys.RightAlt;
        TranslationKeys[Key.BrowserBackward] = Keys.BrowserBack;
        TranslationKeys[Key.BrowserForward] = Keys.BrowserForward;
        TranslationKeys[Key.BrowserRefresh] = Keys.BrowserRefresh;
        TranslationKeys[Key.BrowserStop] = Keys.BrowserStop;
        TranslationKeys[Key.BrowserSearch] = Keys.BrowserSearch;
        TranslationKeys[Key.BrowserFavorites] = Keys.BrowserFavorites;
        TranslationKeys[Key.BrowserHome] = Keys.BrowserHome;
        TranslationKeys[Key.VolumeMute] = Keys.VolumeMute;
        TranslationKeys[Key.VolumeDown] = Keys.VolumeDown;
        TranslationKeys[Key.VolumeUp] = Keys.VolumeUp;
        TranslationKeys[Key.NextTrack] = Keys.MediaNextTrack;
        TranslationKeys[Key.PrevTrack] = Keys.MediaPreviousTrack;
        TranslationKeys[Key.StopMedia] = Keys.MediaStop;
        TranslationKeys[Key.PlayPauseMedia] = Keys.MediaPlayPause;
        TranslationKeys[Key.LaunchMail] = Keys.LaunchMail;
        TranslationKeys[Key.LaunchMediaSelect] = Keys.SelectMedia;
        TranslationKeys[Key.LaunchApp1] = Keys.LaunchApplication1;
        TranslationKeys[Key.LaunchApp2] = Keys.LaunchApplication2;
        TranslationKeys[Key.OemSemicolon] = Keys.OemSemicolon;
        TranslationKeys[Key.OemPlus] = Keys.OemPlus;
        TranslationKeys[Key.OemComma] = Keys.OemComma;
        TranslationKeys[Key.OemMinus] = Keys.OemMinus;
        TranslationKeys[Key.OemPeriod] = Keys.OemPeriod;
        TranslationKeys[Key.OemQuestion] = Keys.OemQuestion;
        TranslationKeys[Key.OemTilde] = Keys.OemTilde;
        TranslationKeys[Key.OemOpenBrackets] = Keys.OemOpenBrackets;
        TranslationKeys[Key.OemPipe] = Keys.OemPipe;
        TranslationKeys[Key.OemCloseBrackets] = Keys.OemCloseBrackets;
        TranslationKeys[Key.OemQuotes] = Keys.OemQuotes;
        TranslationKeys[Key.OemBackSlash] = Keys.OemBackslash;
        TranslationKeys[Key.CrSel] = Keys.Crsel;
        TranslationKeys[Key.ExSel] = Keys.Exsel;
        TranslationKeys[Key.OemClear] = Keys.OemClear;
        TranslationKeys[Key.Shift] = Keys.Shift;
        TranslationKeys[Key.Ctrl] = Keys.Control;
        TranslationKeys[Key.Alt] = Keys.Alt;

        MouseTranslationKeys = new Dictionary<MouseButtons, UniverseMouseButton>();
        MouseTranslationKeys[MouseButtons.Left] = UniverseMouseButton.Left;
        MouseTranslationKeys[MouseButtons.Middle] = UniverseMouseButton.Middle;
        MouseTranslationKeys[MouseButtons.Right] = UniverseMouseButton.Right;
        MouseTranslationKeys[MouseButtons.None] = UniverseMouseButton.None;
        MouseTranslationKeys[MouseButtons.XButton1] = UniverseMouseButton.XButton1;
        MouseTranslationKeys[MouseButtons.XButton2] = UniverseMouseButton.XButton2;
    }

    public UniverseInputListener(IInputComponent component, IUniverseInputTarget target)
    {
        _component = component;
        _target = target;
        // Handled ones too: a modifier key is state the universe has to know - Alt with a drag orbits a camera - even
        // when the ribbon has taken the key for its key tips.
        component.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        component.AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler(OnKeyUp), handledEventsToo: true);
        component.TextInput += OnTextInput;
        component.PointerPressed += OnPointerPressed;
        component.PointerReleased += OnPointerReleased;
        component.PointerWheelChanged += OnPointerWheelChanged;
        component.RawMouseMove += OnRawMouseMove;
        component.SetValue(Gestures.IsManipulationEnabledProperty, true, ValuePriority.Default);
        component.SetValue(Gestures.IsTrackpadPanEnabledProperty, true, ValuePriority.Default);
        component.AddHandler(Gestures.ManipulationEvent, new ManipulationEventHandler(OnManipulation));
        component.AddHandler(PointerEvents.PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled),
            handledEventsToo: true);
    }

    public void Dispose()
    {
        _component.RemoveHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnKeyDown));
        _component.RemoveHandler(Keyboard.KeyUpEvent, new KeyEventHandler(OnKeyUp));
        _component.TextInput -= OnTextInput;
        _component.PointerPressed -= OnPointerPressed;
        _component.PointerReleased -= OnPointerReleased;
        _component.PointerWheelChanged -= OnPointerWheelChanged;
        _component.RawMouseMove -= OnRawMouseMove;
        _component.ClearValue(Gestures.IsManipulationEnabledProperty, ValuePriority.Default);
        _component.ClearValue(Gestures.IsTrackpadPanEnabledProperty, ValuePriority.Default);
        _component.RemoveHandler(Gestures.ManipulationEvent, new ManipulationEventHandler(OnManipulation));
        _component.RemoveHandler(PointerEvents.PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled));
    }

    internal static bool TryTranslate(Key key, uint physicalKey, out Keys result)
    {
        if (physicalKey != 0 && KnownKeys.Contains((Keys)physicalKey))
        {
            result = (Keys)physicalKey;
            return true;
        }

        return TranslationKeys.TryGetValue(key, out result);
    }

    private void OnPointerCanceled(object sender, PointerEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            _target.TakeMouseInput(new MouseInput { InputType = InputType.Up, Button = UniverseMouseButton.Left });
        }

        if (e.Properties.IsRightButtonPressed)
        {
            _target.TakeMouseInput(new MouseInput { InputType = InputType.Up, Button = UniverseMouseButton.Right });
        }
    }

    private void OnManipulation(object sender, ManipulationEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _component) || e.State != ManipulationState.Delta)
        {
            return;
        }

        if (e.Translation != Vector2<double>.Zero)
        {
            HoldAlt(e.Modifiers);
        }

        _target.TakeGestureInput(new GestureInput
        {
            Translation = new Vector2F((float)e.Translation.X, (float)e.Translation.Y) * _target.PixelsPerPoint,
            Scale = (float)e.Scale,
            Rotation = (float)e.Rotation
        });
        e.Handled = true;
    }

    // Alt with a pan orbits too. A gesture does not press the view, so it is focused here, for Alt's release to come to it;
    // in a window that is not active the keyboard is not its.
    private void HoldAlt(InputModifiers modifiers)
    {
        if ((modifiers & (InputModifiers.LeftAlt | InputModifiers.RightAlt)) != 0 &&
            _component.RootVisual?.Window is { IsActive: true } &&
            (_component.IsKeyboardFocused || _component.Focus()))
        {
            HoldModifier(modifiers, InputModifiers.LeftAlt, Keys.LeftAlt);
            HoldModifier(modifiers, InputModifiers.RightAlt, Keys.RightAlt);
        }
    }

    private void OnRawMouseMove(object sender, UnboundMouseEventArgs e)
    {
        _target.TakeMouseInput(new MouseInput { InputType = InputType.RawDelta, Delta = e.Delta.As<float>() });
    }

    private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (!_target.IsUniverseInput(e))
        {
            return;
        }

        // Where it was turned, in the surface's pixels: the universe reads the pointer a frame later, by when it may be
        // elsewhere - or, under automation, never moved there at all.
        var at = e.GetPosition(_component);
        _target.TakeMouseInput(new MouseInput
        {
            InputType = InputType.Wheel,
            WheelDelta = e.Delta,
            Position = new Vector2F((float)at.X, (float)at.Y) * _target.PixelsPerPoint
        });
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!MouseTranslationKeys.TryGetValue(e.ChangedButton, out var button))
        {
            return;
        }

        _target.TakeMouseInput(new MouseInput { InputType = InputType.Up, Button = button });
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!_target.IsUniverseInput(e) || !MouseTranslationKeys.TryGetValue(e.ChangedButton, out var button))
        {
            return;
        }

        HoldModifiers(e.Modifiers);
        _target.TakeMouseInput(new MouseInput { InputType = InputType.Down, Button = button, ClickCount = e.ClickCount });
    }

    // The modifiers held as the button goes down reach the universe whoever had the keyboard when they went down - Alt
    // pressed over the hierarchy, then the right button over the view, orbits. The press focuses the view, so their
    // release comes to it.
    private void HoldModifiers(InputModifiers modifiers)
    {
        HoldModifier(modifiers, InputModifiers.LeftAlt, Keys.LeftAlt);
        HoldModifier(modifiers, InputModifiers.RightAlt, Keys.RightAlt);
        HoldModifier(modifiers, InputModifiers.LeftControl, Keys.LeftControl);
        HoldModifier(modifiers, InputModifiers.RightControl, Keys.RightControl);
        HoldModifier(modifiers, InputModifiers.LeftShift, Keys.LeftShift);
        HoldModifier(modifiers, InputModifiers.RightShift, Keys.RightShift);
    }

    private void HoldModifier(InputModifiers modifiers, InputModifiers modifier, Keys key)
    {
        if ((modifiers & modifier) != 0 && !_target.IsKeyDown(key))
        {
            _target.TakeKeyboardInput(new KeyboardInput { Key = key, InputType = InputType.Down });
        }
    }

    // A key let go reaches the universe whoever handled it: one it saw go down would otherwise stay down there.
    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!TryTranslate(e.Key, e.PhysicalKey, out var key))
        {
            return;
        }

        _target.TakeKeyboardInput(new KeyboardInput { Key = key, InputType = InputType.Up });
    }

    private void OnTextInput(object sender, TextInputEventArgs e)
    {
        if (!_target.IsUniverseInput(e))
        {
            return;
        }

        _target.TakeText(e.Text);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Handled && !e.Key.IsModifier()) || !_target.IsUniverseInput(e) || !TryTranslate(e.Key, e.PhysicalKey, out var key))
        {
            return;
        }

        _target.TakeKeyboardInput(new KeyboardInput { Key = key, InputType = InputType.Down });
        if (_target.TakesKeyboard && _target.SpendsKey(e))
        {
            e.Handled = true;
        }
    }
}
