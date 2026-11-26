using System;
using System.Globalization;
using Adamantium.Localization;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A number with two step buttons. <see cref="Value"/> is nullable because an empty box is not zero, which is why
/// it derives from <see cref="RangeLimitsBase"/> rather than <see cref="RangeBase"/>.</summary>
public class NumericUpDown : RangeLimitsBase
{
    private TextBox _text;
    private RepeatButton _increase;
    private RepeatButton _decrease;
    private bool _writingText;     // the value is writing the text; the text must not write back
    private long _lastStepTime;    // for the speed-up: repeats arrive close together, a fresh press does not
    private int _heldSteps;

    /// <summary>The number, or NOTHING: an entry box can be cleared, and an empty box is not a box holding zero. Every
    /// number that gets in is clamped into [Minimum, Maximum]; null passes straight through, being outside neither.</summary>
    public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
        typeof(double?), typeof(NumericUpDown),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    /// <summary>Prompt text shown while the box is empty and unfocused - the entry box's own
    /// <see cref="TextBoxBase.Placeholder"/>, forwarded so it can be set without reaching into the template.</summary>
    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(string), typeof(NumericUpDown), new PropertyMetadata(null, OnPlaceholderChanged));

    public static readonly AdamantiumProperty ButtonsPlacementProperty = AdamantiumProperty.Register(
        nameof(ButtonsPlacement), typeof(NumericButtonsPlacement), typeof(NumericUpDown),
        new PropertyMetadata(NumericButtonsPlacement.Split));

    /// <summary>Swaps the two buttons, whatever <see cref="ButtonsPlacement"/> they are in.</summary>
    public static readonly AdamantiumProperty AreButtonsSwappedProperty = AdamantiumProperty.Register(
        nameof(AreButtonsSwapped), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    /// <summary>Which way the number and its buttons run. Vertical, the box is as narrow as its number: a
    /// <see cref="NumericButtonsPlacement.Split"/> pair stands over and under it, and its <see cref="Unit"/> under it.
    /// The theme turns a box standing on the column of a tool bar down the side
    /// (<see cref="ToolBar.CommandOrientationProperty"/>), unless it is set.</summary>
    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(Orientation), typeof(NumericUpDown),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>What the number counts - cm, °, pt - shown beside it, or under it when <see cref="Orientation"/> is
    /// vertical. Not part of the number: it is neither typed nor read back.</summary>
    public static readonly AdamantiumProperty UnitProperty = AdamantiumProperty.Register(nameof(Unit),
        typeof(string), typeof(NumericUpDown),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure,
            static (a, e) => ((NumericUpDown)a).HasUnit = !string.IsNullOrEmpty((string)e.NewValue)));

    public static readonly AdamantiumProperty HasUnitProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasUnit),
        typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty AreButtonsVisibleProperty = AdamantiumProperty.Register(
        nameof(AreButtonsVisible), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ButtonsWidthProperty = AdamantiumProperty.Register(
        nameof(ButtonsWidth), typeof(double), typeof(NumericUpDown), new PropertyMetadata(28.0));

    /// <summary>Whether the buttons take keyboard focus when clicked. Off by default: focus belongs in the text, so a
    /// click on a button does not cost you the caret.</summary>
    public static readonly AdamantiumProperty AreButtonsFocusableProperty = AdamantiumProperty.Register(
        nameof(AreButtonsFocusable), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IncreaseButtonContentProperty = AdamantiumProperty.Register(
        nameof(IncreaseButtonContent), typeof(object), typeof(NumericUpDown), new PropertyMetadata("+"));

    public static readonly AdamantiumProperty DecreaseButtonContentProperty = AdamantiumProperty.Register(
        nameof(DecreaseButtonContent), typeof(object), typeof(NumericUpDown), new PropertyMetadata("−"));

    /// <summary>How the value is written out: either a plain .NET numeric format ("N2", "P0") or a full composite
    /// format string ("{0:N2} kg"). Empty = the culture's own default.</summary>
    public static readonly AdamantiumProperty StringFormatProperty = AdamantiumProperty.Register(
        nameof(StringFormat), typeof(string), typeof(NumericUpDown), new PropertyMetadata(null, OnFormatChanged));

    /// <summary>Culture used to write and to read the number back. Null = the current culture.</summary>
    public static readonly AdamantiumProperty CultureProperty = AdamantiumProperty.Register(
        nameof(Culture), typeof(CultureInfo), typeof(NumericUpDown), new PropertyMetadata(null, OnFormatChanged));

    public static readonly AdamantiumProperty InputModeProperty = AdamantiumProperty.Register(
        nameof(InputMode), typeof(NumericInputMode), typeof(NumericUpDown),
        new PropertyMetadata(NumericInputMode.Decimals));

    public static readonly AdamantiumProperty ParsingNumberStyleProperty = AdamantiumProperty.Register(
        nameof(ParsingNumberStyle), typeof(NumberStyles), typeof(NumericUpDown),
        new PropertyMetadata(NumberStyles.Any));

    public static readonly AdamantiumProperty IsManualEntryEnabledProperty = AdamantiumProperty.Register(
        nameof(IsManualEntryEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));

    /// <summary>Read the number back on every keystroke rather than when the entry is committed. Off by default: a
    /// half-typed number is a number too, so "5" on its way to "50" is clamped to a Minimum of 10 and the rest of the
    /// keystrokes have nothing left to build on.</summary>
    public static readonly AdamantiumProperty ChangesValueWhileTypingProperty = AdamantiumProperty.Register(
        nameof(ChangesValueWhileTyping), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty AreArrowKeysEnabledProperty = AdamantiumProperty.Register(
        nameof(AreArrowKeysEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsMouseWheelEnabledProperty = AdamantiumProperty.Register(
        nameof(IsMouseWheelEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));

    /// <summary>Let the wheel step the value while the pointer is merely OVER the control, without it holding focus.
    /// Off by default, because a control that eats the wheel unasked stops the page it sits on from scrolling.</summary>
    public static readonly AdamantiumProperty TracksMouseWheelWhenOverProperty = AdamantiumProperty.Register(
        nameof(TracksMouseWheelWhenOver), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsSpeedupEnabledProperty = AdamantiumProperty.Register(
        nameof(IsSpeedupEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));

    /// <summary>Press the box and drag sideways to run the value up and down, the way a property panel in a 3D tool
    /// does. OFF by default and deliberately so: it takes the press over, so dragging inside the box no longer selects
    /// text (double- and triple-click still do). Pair it with <see cref="AreButtonsVisible"/> off for the bare field
    /// those tools actually show.</summary>
    public static readonly AdamantiumProperty IsDragScrubEnabledProperty = AdamantiumProperty.Register(
        nameof(IsDragScrubEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    /// <summary>How far the pointer travels for one step of the drag. In pixels PER STEP rather than units per pixel, so
    /// the feel stays put when <see cref="RangeLimitsBase.SmallChange"/> changes.</summary>
    public static readonly AdamantiumProperty ScrubPixelsPerStepProperty = AdamantiumProperty.Register(
        nameof(ScrubPixelsPerStep), typeof(double), typeof(NumericUpDown), new PropertyMetadata(4.0));

    /// <summary>The pointers shown while a scrub drag runs left and right; both default to <see cref="Cursors.SizeWE"/>
    /// until set to one-way shapes.</summary>
    public static readonly AdamantiumProperty ScrubLeftCursorProperty = AdamantiumProperty.Register(
        nameof(ScrubLeftCursor), typeof(Cursor), typeof(NumericUpDown), new PropertyMetadata(Cursors.SizeWE));

    public static readonly AdamantiumProperty ScrubRightCursorProperty = AdamantiumProperty.Register(
        nameof(ScrubRightCursor), typeof(Cursor), typeof(NumericUpDown), new PropertyMetadata(Cursors.SizeWE));

    /// <summary>Round the value to a whole number of <see cref="RangeLimitsBase.SmallChange"/> steps.</summary>
    public static readonly AdamantiumProperty SnapsToSmallChangeProperty = AdamantiumProperty.Register(
        nameof(SnapsToSmallChange), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsReadOnlyProperty = AdamantiumProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

    public static readonly AdamantiumProperty DelayProperty = AdamantiumProperty.Register(nameof(Delay),
        typeof(int), typeof(NumericUpDown), new PropertyMetadata(0));

    public static readonly AdamantiumProperty RepeatIntervalProperty = AdamantiumProperty.Register(
        nameof(RepeatInterval), typeof(int), typeof(NumericUpDown), new PropertyMetadata(0));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(NumericUpDown), new PropertyMetadata(null, OnCommandChanged));

    private CommandSourceListener<NumericUpDown> _commandListener;
    private bool _committing;

    public event EventHandler<NullableValueChangedEventArgs> ValueChanged;

    /// <summary>What a number the user puts in runs, given the number, as a ribbon's width box sets the width of the
    /// picture selected: a step, a scrub, the wheel or a typed number; a value set from outside or moved by the limits
    /// runs nothing. The focus stays in the box while it is stepped or typed in, and goes back to the field the
    /// command acted on with Enter. The number a routed command answers with is shown, so the box follows what it
    /// stands for; <see cref="Value"/> then follows the command, not a binding. While the command cannot run, the box
    /// is disabled.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The value ran into <see cref="RangeLimitsBase.Minimum"/> - a step asked for less than the range holds.</summary>
    public event EventHandler MinimumReached;

    /// <summary>The value ran into <see cref="RangeLimitsBase.Maximum"/>.</summary>
    public event EventHandler MaximumReached;

    /// <summary>Whether the value is being moved and moves on: a button held, a drag, an arrow key held, the wheel turning.
    /// A value changed meanwhile is not settled yet; <see cref="InteractionEnded"/> says when it is.</summary>
    public bool IsInteracting => _interaction != Interaction.None;

    /// <summary>The value stopped moving: the button or key let go, the drag ended, the pointer or the focus gone after
    /// the wheel.</summary>
    public event EventHandler InteractionEnded;

    private Interaction _interaction;

    private void Interact(Interaction way)
    {
        // A drag or a button held goes on through a key or the wheel meanwhile: it is still the move.
        if (_interaction == way || _interaction is Interaction.Drag or Interaction.Button && way is Interaction.Keys or Interaction.Wheel)
            return;

        EndInteraction();
        _interaction = way;
    }

    private void EndInteraction()
    {
        if (_interaction == Interaction.None)
            return;

        _interaction = Interaction.None;
        _heldStepKeys.Clear();
        InteractionEnded?.Invoke(this, EventArgs.Empty);
    }

    // How the value is being moved.
    private enum Interaction
    {
        None,
        Button,
        Drag,
        Keys,
        Wheel
    }

    public double? Value
    {
        get => GetValue<double?>(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static NumericUpDown()
    {
        // Unbounded unless told otherwise: a spinner over the 0..1 that RangeLimitsBase defaults to could not count to
        // two. Via metadata, never a constructor set - a set writes Local priority and would mask a {Binding} forever.
        MinimumProperty.OverrideMetadata(typeof(NumericUpDown), new PropertyMetadata(double.MinValue));
        MaximumProperty.OverrideMetadata(typeof(NumericUpDown), new PropertyMetadata(double.MaxValue));
    }

    public NumericUpDown()
    {
        AddHandler(Keyboard.PreviewTextInputEvent, new TextInputEventHandler(OnPreviewTextInput));
        AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelEventHandler(OnWheelStep));
        // Handled ones too: the text fills the control and takes the press for its caret, yet a press there still scrubs.
        AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler(OnScrubPressed), handledEventsToo: true);
        AddHandler(PointerEvents.PointerMovedEvent, new PointerEventHandler(OnScrubMoved), handledEventsToo: true);
        AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnScrubReleased), handledEventsToo: true);
    }

    public string Placeholder
    {
        get => GetValue<string>(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public NumericButtonsPlacement ButtonsPlacement
    {
        get => GetValue<NumericButtonsPlacement>(ButtonsPlacementProperty);
        set => SetValue(ButtonsPlacementProperty, value);
    }

    public bool AreButtonsSwapped
    {
        get => GetValue<bool>(AreButtonsSwappedProperty);
        set => SetValue(AreButtonsSwappedProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public string Unit
    {
        get => GetValue<string>(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>Whether a <see cref="Unit"/> is shown.</summary>
    public bool HasUnit
    {
        get => GetValue<bool>(HasUnitProperty);
        private set => SetReadOnlyValue(HasUnitProperty, value);
    }

    public bool AreButtonsVisible
    {
        get => GetValue<bool>(AreButtonsVisibleProperty);
        set => SetValue(AreButtonsVisibleProperty, value);
    }

    public double ButtonsWidth
    {
        get => GetValue<double>(ButtonsWidthProperty);
        set => SetValue(ButtonsWidthProperty, value);
    }

    public bool AreButtonsFocusable
    {
        get => GetValue<bool>(AreButtonsFocusableProperty);
        set => SetValue(AreButtonsFocusableProperty, value);
    }

    public object IncreaseButtonContent
    {
        get => GetValue<object>(IncreaseButtonContentProperty);
        set => SetValue(IncreaseButtonContentProperty, value);
    }

    public object DecreaseButtonContent
    {
        get => GetValue<object>(DecreaseButtonContentProperty);
        set => SetValue(DecreaseButtonContentProperty, value);
    }

    public string StringFormat
    {
        get => GetValue<string>(StringFormatProperty);
        set => SetValue(StringFormatProperty, value);
    }

    public CultureInfo Culture
    {
        get => GetValue<CultureInfo>(CultureProperty);
        set => SetValue(CultureProperty, value);
    }

    public NumericInputMode InputMode
    {
        get => GetValue<NumericInputMode>(InputModeProperty);
        set => SetValue(InputModeProperty, value);
    }

    public NumberStyles ParsingNumberStyle
    {
        get => GetValue<NumberStyles>(ParsingNumberStyleProperty);
        set => SetValue(ParsingNumberStyleProperty, value);
    }

    public bool IsManualEntryEnabled
    {
        get => GetValue<bool>(IsManualEntryEnabledProperty);
        set => SetValue(IsManualEntryEnabledProperty, value);
    }

    public bool ChangesValueWhileTyping
    {
        get => GetValue<bool>(ChangesValueWhileTypingProperty);
        set => SetValue(ChangesValueWhileTypingProperty, value);
    }

    public bool AreArrowKeysEnabled
    {
        get => GetValue<bool>(AreArrowKeysEnabledProperty);
        set => SetValue(AreArrowKeysEnabledProperty, value);
    }

    public bool IsMouseWheelEnabled
    {
        get => GetValue<bool>(IsMouseWheelEnabledProperty);
        set => SetValue(IsMouseWheelEnabledProperty, value);
    }

    public bool TracksMouseWheelWhenOver
    {
        get => GetValue<bool>(TracksMouseWheelWhenOverProperty);
        set => SetValue(TracksMouseWheelWhenOverProperty, value);
    }

    public bool IsSpeedupEnabled
    {
        get => GetValue<bool>(IsSpeedupEnabledProperty);
        set => SetValue(IsSpeedupEnabledProperty, value);
    }

    public bool IsDragScrubEnabled
    {
        get => GetValue<bool>(IsDragScrubEnabledProperty);
        set => SetValue(IsDragScrubEnabledProperty, value);
    }

    public double ScrubPixelsPerStep
    {
        get => GetValue<double>(ScrubPixelsPerStepProperty);
        set => SetValue(ScrubPixelsPerStepProperty, value);
    }

    public Cursor ScrubLeftCursor
    {
        get => GetValue<Cursor>(ScrubLeftCursorProperty);
        set => SetValue(ScrubLeftCursorProperty, value);
    }

    public Cursor ScrubRightCursor
    {
        get => GetValue<Cursor>(ScrubRightCursorProperty);
        set => SetValue(ScrubRightCursorProperty, value);
    }

    public bool SnapsToSmallChange
    {
        get => GetValue<bool>(SnapsToSmallChangeProperty);
        set => SetValue(SnapsToSmallChangeProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue<bool>(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Milliseconds a button must be held before it starts repeating; 0, the default, as a held key in the OS.
    /// </summary>
    public int Delay
    {
        get => GetValue<int>(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>Milliseconds between repeats once a held button has started repeating; 0, the default, as a held key in
    /// the OS.</summary>
    public int RepeatInterval
    {
        get => GetValue<int>(RepeatIntervalProperty);
        set => SetValue(RepeatIntervalProperty, value);
    }

    /// <summary>Puts the caret in the number with all of it selected, so what is typed replaces it. False before the
    /// template, or while the text itself cannot take focus.</summary>
    public bool FocusText()
    {
        if (_text == null || !_text.Focus())
        {
            return false;
        }

        _text.SelectAll();
        return true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();   // a template swap re-runs this; drop the old wiring first

        _text = GetTemplateChild("PART_TextBox") as TextBox;
        _increase = GetTemplateChild("PART_Increase") as RepeatButton;
        _decrease = GetTemplateChild("PART_Decrease") as RepeatButton;

        if (_text != null)
        {
            _text.EnterPressed += OnTextEnterPressed;
            _text.LostFocus += OnTextLostFocus;
            _text.PropertyChanged += OnTextChanged;
            _text.Placeholder = Placeholder;
        }
        if (_increase != null)
        {
            _increase.Click += OnIncreaseClick;
            _increase.PropertyChanged += OnButtonPropertyChanged;
        }

        if (_decrease != null)
        {
            _decrease.Click += OnDecreaseClick;
            _decrease.PropertyChanged += OnButtonPropertyChanged;
        }

        UpdateText();
        UpdateButtons();
    }

    // A button let go ends its hold.
    private void OnButtonPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ButtonBase.IsPressedProperty && e.NewValue is false && _interaction == Interaction.Button)
            EndInteraction();
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
    }

    private void DetachParts()
    {
        if (_text != null)
        {
            _text.EnterPressed -= OnTextEnterPressed;
            _text.LostFocus -= OnTextLostFocus;
            _text.PropertyChanged -= OnTextChanged;
            _text = null;
        }
        if (_increase != null)
        {
            _increase.Click -= OnIncreaseClick;
            _increase.PropertyChanged -= OnButtonPropertyChanged;
            _increase = null;
        }
        if (_decrease != null)
        {
            _decrease.Click -= OnDecreaseClick;
            _decrease.PropertyChanged -= OnButtonPropertyChanged;
            _decrease = null;
        }

        EndInteraction();
    }

    // Null is not out of range - it is the absence of one - so it passes through untouched; a number is clamped, the
    // same rule RangeBase applies to its own Value.
    private static object CoerceValue(AdamantiumComponent d, object baseValue)
    {
        if (baseValue is not double value) return baseValue;

        var box = (NumericUpDown)d;
        if (value < box.Minimum) value = box.Minimum;
        if (value > box.Maximum) value = box.Maximum;
        return value;
    }

    private static void OnPlaceholderChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is NumericUpDown numeric && numeric._text != null) numeric._text.Placeholder = e.NewValue as string;
    }

    private static void OnValueChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        var box = (NumericUpDown)d;
        // The sentinel a property carries before it was ever set is not a number and not null-the-value; `as` maps both
        // to null here, which is what an empty box holds anyway.
        var oldValue = e.OldValue as double?;
        var newValue = e.NewValue as double?;

        box.UpdateText();
        box.UpdateButtons();

        if (newValue.HasValue)
        {
            if (newValue.Value <= box.Minimum) box.MinimumReached?.Invoke(box, EventArgs.Empty);
            if (newValue.Value >= box.Maximum) box.MaximumReached?.Invoke(box, EventArgs.Empty);
        }

        box.ValueChanged?.Invoke(box, new NullableValueChangedEventArgs(oldValue, newValue));
    }

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var box = (NumericUpDown)a;
        box._commandListener ??= new CommandSourceListener<NumericUpDown>(box, static b => b.UpdateCommandState());
        box._commandListener.Listen(e.NewValue as ICommand);
        box.UpdateCommandState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _commandListener?.Attached();
    }

    // The culture the number was last written in, as far as the language decides it.
    private CultureInfo _written = Languages.Culture;

    // The number is written the new language's way; one half typed is not touched.
    protected override void OnLanguageChanged()
    {
        var before = Culture ?? _written;
        _written = Languages.Culture;
        if (_text != null && Value is { } number && _text.Text == FormatValue(number, before)) UpdateText();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _commandListener?.Detached();
    }

    // Whether the command can run; a field with none can be used.
    private bool _canExecute = true;

    protected override bool IsEnabledCore => base.IsEnabledCore && _canExecute;

    private void UpdateCommandState()
    {
        var state = CommandManager.QueryFrom(Command, null, this, null);
        _canExecute = state.CanExecute;
        CoerceValue(IsEnabledProperty);
        if (Command is RoutedCommand)
        {
            SetCurrentValue(ValueProperty, state.Value switch
            {
                double number => (double?)number,
                float number => number,
                long number => number,
                int number => number,
                decimal number => (double)number,
                _ => null,
            });
        }
    }

    /// <summary>A limit moved: re-map the request through the coercion, so a value clamped by the old range is restored
    /// when the new one has room for it.</summary>
    protected override void ReCoerceSelection() => CoerceValue(ValueProperty);

    protected override void OnLimitsChanged()
    {
        // The value may have been re-clamped, and either end may have just become reachable or unreachable.
        UpdateText();
        UpdateButtons();
    }

    private static void OnFormatChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        (a as NumericUpDown)?.UpdateText();
    }

    // --- Text <-> value ---------------------------------------------------------------------------------------------

    private CultureInfo EffectiveCulture => Culture ?? Adamantium.Localization.Languages.Culture;

    /// <summary>The value as it is shown. A format holding a placeholder is a composite one ("{0:N2} kg"); anything
    /// else is a plain numeric format ("N2") - the same two shapes MahApps accepts.</summary>
    private string FormatValue(double value) => FormatValue(value, EffectiveCulture);

    private string FormatValue(double value, CultureInfo culture)
    {
        var format = StringFormat;
        if (string.IsNullOrEmpty(format)) return value.ToString(culture);
        return format.Contains('{')
            ? string.Format(culture, format, value)
            : value.ToString(format, culture);
    }

    private bool TryParse(string text, out double value)
        => double.TryParse(text, ParsingNumberStyle, EffectiveCulture, out value);

    private string ValueText() => Value.HasValue ? FormatValue(Value.Value) : string.Empty;

    // Whether the box shows an entry not taken yet - what Escape abandons.
    private bool IsEntryPending() => _text != null && _text.Text != ValueText();

    private void UpdateText()
    {
        if (_text == null) return;

        var text = ValueText();
        if (_text.Text == text) return;

        _writingText = true;
        try
        {
            _text.Text = text;
            // The caret indexed the number that was there BEFORE, so leaving it be strands it somewhere arbitrary
            // inside the new one - park it after the last digit, which is where anyone about to type wants it anyway.
            // This is not only about the drag: a stepped 9 -> 10 left the caret between the digits just the same.
            _text.CaretIndex = text.Length;
            _text.SelectionStart = text.Length;
            _text.SelectionLength = 0;
        }
        finally { _writingText = false; }
    }

    private void OnTextChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty) return;
        // Our own write, echoed back. Reading it as an edit would re-parse what we just formatted, and a format that
        // rounds ("N0" on 2.4) would quietly move the value every time it was displayed.
        if (_writingText || !ChangesValueWhileTyping || !IsManualEntryEnabled) return;

        ReadEntry();
    }

    /// <summary>Take what is typed as the value. An empty box is EMPTY, not zero - that is the whole reason the value is
    /// nullable - and anything that is not a number at all is simply not taken.</summary>
    private bool ReadEntry()
    {
        if (string.IsNullOrWhiteSpace(_text.Text))
        {
            SetCurrentValue(ValueProperty, null);
            return true;
        }

        if (Value is { } shown && _text.Text == FormatValue(shown))
        {
            return true;
        }

        if (!TryParse(_text.Text, out var parsed))
        {
            return false;
        }

        SetValueFromInput(parsed);
        return true;
    }

    private void OnTextEnterPressed(object sender, KeyEventArgs e)
    {
        _committing = true;
        try
        {
            e.Handled = !CommitText();
        }
        finally
        {
            _committing = false;
        }
    }

    private void OnTextLostFocus(object sender, RoutedEventArgs e)
    {
        CommitText();
        EndInteraction();
    }

    /// <summary>Read the entry back, then write the value out again - which both re-formats what parsed and restores
    /// what did not, so the box can never be left showing something the value is not.</summary>
    private bool CommitText()
    {
        if (_text == null) return true;

        var read = !IsManualEntryEnabled || IsReadOnly || ReadEntry();
        UpdateText();
        return read;
    }

    // --- Stepping ---------------------------------------------------------------------------------------------------

    // The step buttons are the box's own parts: their click ends here, not in whatever the box stands in.
    private void OnIncreaseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        StepByButton(sender, +1);
    }

    private void OnDecreaseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        StepByButton(sender, -1);
    }

    // A button held repeats its step until let go; a click by the keyboard is one step, settled at once.
    private void StepByButton(object button, int direction)
    {
        if (button is ButtonBase { IsPressed: true })
            Interact(Interaction.Button);
        else
            EndInteraction();

        Step(direction, HeldStep());
    }

    private void Step(int direction, double amount)
    {
        if (IsReadOnly) return;
        SetValueFromInput((Value ?? StartingPoint()) + direction * amount);
    }

    /// <summary>Where a step starts from when the box is empty: zero, unless the range does not reach it, in which case
    /// the end that is nearest. Stepping an empty box has to begin SOMEWHERE, and beginning outside the range would only
    /// be clamped back anyway.</summary>
    private double StartingPoint()
    {
        if (Minimum > 0) return Minimum;
        if (Maximum < 0) return Maximum;
        return 0;
    }

    /// <summary>The step for one click of a held button. Repeats arrive one <see cref="RepeatInterval"/> apart, so a
    /// click that follows closely enough is part of a hold: after a few of those the step doubles every so often, up to
    /// a cap, so crossing a big range does not need the button held for a minute - and a single click stays a step.</summary>
    private double HeldStep()
    {
        if (!IsSpeedupEnabled) return SmallChange;

        var now = Environment.TickCount64;
        var continues = now - _lastStepTime <= Math.Max(100, RepeatButton.IntervalOrTheUsers(RepeatInterval) * 3);
        _heldSteps = continues ? _heldSteps + 1 : 0;
        _lastStepTime = now;

        var doublings = Math.Min(6, Math.Max(0, _heldSteps - 10) / 15);
        return SmallChange * (1 << doublings);
    }

    private void SetValueFromInput(double value) => PutIn(Snap(value));

    internal void PutIn(double value)
    {
        // SetCurrentValue, not a plain assignment: a Local write outranks a {Binding} and would freeze a data-bound
        // control for good - the source could never move it again (the same note as on Slider).
        var before = Value;
        SetCurrentValue(ValueProperty, value);
        if (Command is { } command && Value is { } number && number != before)
        {
            CommandManager.ExecuteFrom(command, number, this, null, _committing);
        }
    }

    /// <summary>Rounded to a whole number of steps FROM ZERO, so 0.1-steps land on 0.3 rather than on 0.30000000000004,
    /// and so an unbounded Minimum (which the default is) cannot turn the arithmetic into an infinity.</summary>
    private double Snap(double value)
    {
        if (!SnapsToSmallChange || SmallChange <= 0) return value;

        var snapped = Math.Round(value / SmallChange, MidpointRounding.AwayFromZero) * SmallChange;
        // To the decimals the step is written with: twelve steps of 0.1 are 1.2, not the product's 1.2000000000000002.
        // A step with no short decimal - a third - is left to the product.
        for (var decimals = 0; decimals <= MaximumStepDecimals; decimals++)
        {
            if (Math.Round(SmallChange, decimals) == SmallChange)
                return Math.Round(snapped, decimals);
        }

        return snapped;
    }

    private const int MaximumStepDecimals = 10;

    private void UpdateButtons()
    {
        // A button that cannot move the value any further says so, rather than clicking to no effect. An EMPTY box can
        // always be stepped - that is how a value gets into it - so neither button is off in that state.
        if (_increase != null) _increase.IsEnabled = !IsReadOnly && (!Value.HasValue || Value.Value < Maximum);
        if (_decrease != null) _decrease.IsEnabled = !IsReadOnly && (!Value.HasValue || Value.Value > Minimum);
    }

    // --- Input ------------------------------------------------------------------------------------------------------

    // The TUNNEL, so an arrow key is a step before the text box reads it as caret movement, which it would mark handled.
    // The wheel bubbles up from the text box, so it is taken on the way back.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // A key taken before it reached here - a window's shortcut, a save, an undo - ends the keys' or the wheel's move
        // all the same: what it does should find the value settled.
        if (e.Handled && _interaction is Interaction.Keys or Interaction.Wheel && !IsModifier(e.Key))
            EndInteraction();

        if (e.Handled || IsReadOnly) return;

        var stepping = AreArrowKeysEnabled && IsStepKey(e.Key);
        if (stepping)
        {
            _heldStepKeys.Add(e.Key);
            Interact(Interaction.Keys);
        }
        // Another key ends the keys' or the wheel's move; a modifier - Shift or Ctrl sizing a drag's step - ends none.
        else if (_interaction is Interaction.Keys or Interaction.Wheel && !IsModifier(e.Key))
        {
            EndInteraction();
        }

        switch (e.Key)
        {
            case Key.UpArrow when stepping:
                Step(+1, SmallChange);
                break;
            case Key.DownArrow when stepping:
                Step(-1, SmallChange);
                break;
            case Key.PageUp when stepping:
                Step(+1, LargeChange);
                break;
            case Key.PageDown when stepping:
                Step(-1, LargeChange);
                break;
            // Abandons an entry, back to the value; with none it is left to the window, which a dialog closes by.
            case Key.Escape when IsEntryPending():
                UpdateText();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // The last step key held let go ends the keys' move.
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (!IsStepKey(e.Key))
            return;

        _heldStepKeys.Remove(e.Key);
        if (_interaction == Interaction.Keys && _heldStepKeys.Count == 0)
            EndInteraction();
    }

    // The step keys down now.
    private readonly HashSet<Key> _heldStepKeys = [];

    private static bool IsStepKey(Key key) => key is Key.UpArrow or Key.DownArrow or Key.PageUp or Key.PageDown;

    private static bool IsModifier(Key key) => key is Key.Shift or Key.Ctrl or Key.Alt or Key.LeftShift or Key.RightShift
        or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftWin or Key.RightWin;

    // A drag whose pointer was taken away - another window, Alt+Tab - ends there: no release will come.
    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Only the dragging pointer's capture gone from it: a part's, taken over as the drag begins, bubbles here too -
        // told as its own, the source set to this control as the event leaves the template - and so does another
        // pointer's.
        if (!_scrubbing || !ReferenceEquals(e.Pointer, _scrubPointer) || ReferenceEquals(_scrubPointer.Captured, this))
            return;

        _scrubArmed = false;
        _scrubbing = false;
        Mouse.OverrideCursor = null;
        _text?.SuppressCaret(false);
        EndInteraction();
    }

    // The wheel has no end of its own: the pointer leaving ends its turning.
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_interaction == Interaction.Wheel)
            EndInteraction();
    }

    /// <summary>Refuse a keystroke that could not be part of a number BEFORE the text box inserts it. The preview and
    /// the main event share one args object and a handled one skips the handlers, so marking it here is what stops the
    /// character - there is nothing to undo afterwards.</summary>
    private void OnPreviewTextInput(object sender, TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text)) return;

        if (!IsManualEntryEnabled || IsReadOnly)
        {
            e.Handled = true;
            return;
        }

        var numbers = EffectiveCulture.NumberFormat;
        foreach (var c in e.Text)
        {
            if (char.IsDigit(c)) continue;
            var isSeparator = InputMode == NumericInputMode.Decimals &&
                              numbers.NumberDecimalSeparator.Contains(c);
            var isSign = numbers.NegativeSign.Contains(c) || numbers.PositiveSign.Contains(c);
            var isGrouping = numbers.NumberGroupSeparator.Contains(c);
            if (isSeparator || isSign || isGrouping) continue;

            e.Handled = true;
            return;
        }
    }

    // --- Drag to scrub -----------------------------------------------------------------------------------------------

    private const double ScrubThreshold = 4;   // pixels a press may wander before it stops being a click

    private bool _scrubArmed;      // a press landed here; a sideways move may yet turn it into a scrub
    private bool _scrubbing;
    private double _scrubOriginX;  // in the ROOT's space - the one frame that cannot move under the drag
    private double _scrubFrom;
    private int _scrubDirection;   // -1 / +1: which side of the press the drag is on, so the pointer only changes on a real turn
    private Pointer _scrubPointer; // the pointer dragging: only its capture going elsewhere ends the drag

    private void OnScrubPressed(object sender, PointerPressedEventArgs e)
    {
        // Any press ends the wheel's turning or another move.
        if (_interaction is Interaction.Wheel or Interaction.Keys)
            EndInteraction();

        if (e.ChangedButton != MouseButtons.Left || !IsDragScrubEnabled || IsReadOnly || e.ClickCount > 1)
        {
            return;
        }

        // A step button's press is its own: a hand drifting while it is held drags nothing.
        if (IsInStepButton(e.OriginalSource as IUIComponent))
        {
            _scrubArmed = _scrubbing;
            return;
        }

        _scrubOriginX = RootX(e);
        _scrubFrom = Value ?? StartingPoint();
        _scrubArmed = true;
    }

    private bool IsInStepButton(IUIComponent pressed)
    {
        for (var node = pressed; node != null && !ReferenceEquals(node, this); node = node.VisualParent)
        {
            if (ReferenceEquals(node, _increase) || ReferenceEquals(node, _decrease))
                return true;
        }

        return false;
    }

    private void OnScrubMoved(object sender, PointerEventArgs e)
    {
        if (!_scrubArmed)
        {
            return;
        }

        var traveled = RootX(e) - _scrubOriginX;
        if (!_scrubbing)
        {
            if (Math.Abs(traveled) < ScrubThreshold)
            {
                return;
            }

            BeginScrub(e.Pointer);
        }

        ShowScrubCursor(traveled);
        SetValueFromInput(_scrubFrom + traveled / Math.Max(1, ScrubPixelsPerStep) * ScrubStep());
    }

    private void OnScrubReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left || !_scrubArmed)
        {
            return;
        }

        _scrubArmed = false;
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        Mouse.OverrideCursor = null;
        _text?.SuppressCaret(false);
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }

        EndInteraction();
    }

    private void BeginScrub(Pointer pointer)
    {
        Interact(Interaction.Drag);
        // The text's selection let go first: it gives up the pointer, which a drag already under way would take for
        // its own end.
        _text?.CancelPointerSelection();
        _text?.SuppressCaret(true);
        _scrubbing = true;
        _scrubPointer = pointer;
        pointer.Capture(this);
        _scrubDirection = 0;
    }

    /// <summary>Shows the pointer for the direction the drag has gone, from the PRESS - not from the last event, which
    /// would flip the shape on every twitch of the hand. Set through the app-wide OVERRIDE rather than Mouse.Cursor: the
    /// pointer keeps entering the parts it drags across (presenter, frame, a button), and each of those sets Mouse.Cursor
    /// to its own on the way in, which wiped ours a frame after we set it.</summary>
    private void ShowScrubCursor(double traveled)
    {
        var direction = Math.Sign(traveled);
        if (direction == 0 || direction == _scrubDirection) return;

        _scrubDirection = direction;
        Mouse.OverrideCursor = (direction < 0 ? ScrubLeftCursor : ScrubRightCursor) ?? Cursors.SizeWE;
    }

    /// <summary>What one step of the drag is worth. The modifiers are the ones every 3D tool uses: Shift for a tenth of
    /// a step, Ctrl for a whole page of them.</summary>
    private double ScrubStep()
    {
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0) return SmallChange / 10;
        if ((modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0) return LargeChange;
        return SmallChange;
    }

    private double RootX(PointerEventArgs e)
    {
        IUIComponent root = this;
        while (root.VisualParent is { } parent) root = parent;
        return root is IInputComponent input ? e.GetPosition(input).X : e.GetPosition(this).X;
    }

    private void OnWheelStep(object sender, PointerWheelEventArgs e)
    {
        if (e.Handled || IsReadOnly || !IsMouseWheelEnabled || e.IsHorizontal || e.Delta == 0)
        {
            return;
        }

        if (_text?.IsFocused != true && !(TracksMouseWheelWhenOver && IsPointerOver))
        {
            return;
        }

        Interact(Interaction.Wheel);
        Step(Math.Sign(e.Delta), SmallChange);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NumericUpDownAutomationPeer(this);
}
