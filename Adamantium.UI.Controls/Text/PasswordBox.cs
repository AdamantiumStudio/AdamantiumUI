using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>An entry for a password: the text is shown as a mask and never leaves the box by the clipboard, while
/// <see cref="Password"/> binds like any other property. Placeholders, the floating one included, are a text box's.</summary>
public class PasswordBox : TextBoxBase
{
    public static readonly AdamantiumProperty PasswordProperty = AdamantiumProperty.Register(nameof(Password),
        typeof(string), typeof(PasswordBox),
        new PropertyMetadata(string.Empty,
            PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static readonly AdamantiumProperty PasswordCharProperty = AdamantiumProperty.Register(nameof(PasswordChar),
        typeof(char), typeof(PasswordBox), new PropertyMetadata((char)0x25CF, OnShownTextChanged));

    public static readonly AdamantiumProperty PasswordRevealModeProperty = AdamantiumProperty.Register(
        nameof(PasswordRevealMode), typeof(PasswordRevealMode), typeof(PasswordBox),
        new PropertyMetadata(PasswordRevealMode.Peek));

    public static readonly AdamantiumProperty IsPasswordRevealedProperty = AdamantiumProperty.Register(
        nameof(IsPasswordRevealed), typeof(bool), typeof(PasswordBox), new PropertyMetadata(false, OnShownTextChanged));

    /// <summary>Whether the box holds a password. The control keeps it; the theme reads it.</summary>
    public static readonly AdamantiumProperty HasPasswordProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HasPassword), typeof(bool), typeof(PasswordBox), new PropertyMetadata(false));

    private const char ContinuesCharacter = (char)0x034F;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private ButtonBase _revealButton;
    private PointerPressedEventHandler _peekPressed;
    private PointerReleasedEventHandler _peekReleased;
    private PointerEventHandler _peekLost;
    private string _maskedFrom;
    private char _maskedWith;
    private string _mask;
    private IWindow _inputMethodOffIn;

    /// <summary>The password. Two-way by default, so a view model can hold it.</summary>
    public string Password
    {
        get => GetValue<string>(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    /// <summary>The character shown for each character of the password.</summary>
    public char PasswordChar
    {
        get => GetValue<char>(PasswordCharProperty);
        set => SetValue(PasswordCharProperty, value);
    }

    /// <summary>How the reveal button shows the password: while pressed (the default), on a click until the next one,
    /// or no button at all - then only <see cref="IsPasswordRevealed"/> shows it, for an application with a control of
    /// its own.</summary>
    public PasswordRevealMode PasswordRevealMode
    {
        get => GetValue<PasswordRevealMode>(PasswordRevealModeProperty);
        set => SetValue(PasswordRevealModeProperty, value);
    }

    /// <summary>Whether the password is shown as it is rather than masked.</summary>
    public bool IsPasswordRevealed
    {
        get => GetValue<bool>(IsPasswordRevealedProperty);
        set => SetValue(IsPasswordRevealedProperty, value);
    }

    public bool HasPassword => GetValue<bool>(HasPasswordProperty);

    protected override AdamantiumProperty EditedTextProperty => PasswordProperty;

    protected override bool KeepsUndoHistory => false;

    protected override string DisplayedText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            ForgetMask();
            return text;
        }

        if (IsPasswordRevealed)
        {
            return text;
        }

        if (ReferenceEquals(text, _maskedFrom) && _maskedWith == PasswordChar)
        {
            return _mask;
        }

        var mask = new StringBuilder(text.Length);
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            mask.Append(PasswordChar);
            mask.Append(ContinuesCharacter, elements.GetTextElement().Length - 1);
        }

        _maskedFrom = text;
        _maskedWith = PasswordChar;
        _mask = mask.ToString();
        return _mask;
    }

    protected override int WordBoundary(int index, int dir) => dir < 0 ? 0 : (Password ?? string.Empty).Length;

    protected override (int Start, int End) WordAt(int index) => (0, (Password ?? string.Empty).Length);

    protected override bool GivesTextAway => false;

    protected override void CopyToClipboard()
    {
    }

    protected override void CutToClipboard()
    {
    }

    /// <summary>Pastes the first line of the text, without control characters: a password copied with the line
    /// break after it must not gain a character nobody sees.</summary>
    protected override void Paste(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        text = (text ?? string.Empty).TrimStart('\r', '\n', LineSeparator, ParagraphSeparator);
        var lineEnd = text.IndexOfAny(['\r', '\n', LineSeparator, ParagraphSeparator]);
        if (lineEnd >= 0)
        {
            text = text[..lineEnd];
        }

        text = string.Concat(text.Where(c => !char.IsControl(c)));
        if (text.Length > 0)
        {
            ReplaceSelection(text);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _revealButton = GetTemplateChild("PART_RevealButton") as ButtonBase;
        if (_revealButton != null)
        {
            _peekPressed ??= OnPeekPressed;
            _peekReleased ??= OnPeekReleased;
            _peekLost ??= (_, _) => Peek(false);
            _revealButton.AddHandler(PointerEvents.PointerPressedEvent, _peekPressed, handledEventsToo: true);
            _revealButton.AddHandler(PointerEvents.PointerReleasedEvent, _peekReleased, handledEventsToo: true);
            _revealButton.AddHandler(PointerEvents.PointerCaptureLostEvent, _peekLost, handledEventsToo: true);
            _revealButton.Click += OnRevealButtonClick;
        }
    }

    public override void OnRemoveTemplate()
    {
        if (_revealButton != null)
        {
            _revealButton.RemoveHandler(PointerEvents.PointerPressedEvent, _peekPressed);
            _revealButton.RemoveHandler(PointerEvents.PointerReleasedEvent, _peekReleased);
            _revealButton.RemoveHandler(PointerEvents.PointerCaptureLostEvent, _peekLost);
            _revealButton.Click -= OnRevealButtonClick;
            Peek(false);
        }

        _revealButton = null;
        base.OnRemoveTemplate();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.OriginalSource, this) && _inputMethodOffIn == null && RootVisual is IWindow window)
        {
            _inputMethodOffIn = window;
            window.SetInputMethodEnabled(false);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (ReferenceEquals(e.OriginalSource, this))
        {
            TurnInputMethodBackOn();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TurnInputMethodBackOn();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PasswordBoxAutomationPeer(this);

    private void OnPeekPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton == MouseButtons.Left)
        {
            Peek(true);
        }
    }

    private void OnPeekReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.ChangedButton == MouseButtons.Left)
        {
            Peek(false);
        }
    }

    private void Peek(bool shown)
    {
        if (PasswordRevealMode == PasswordRevealMode.Peek && IsPasswordRevealed != shown)
        {
            SetCurrentValue(IsPasswordRevealedProperty, shown);
        }
    }

    private void TurnInputMethodBackOn()
    {
        _inputMethodOffIn?.SetInputMethodEnabled(true);
        _inputMethodOffIn = null;
    }

    private void ForgetMask()
    {
        _maskedFrom = null;
        _mask = null;
    }

    private void OnRevealButtonClick(object sender, RoutedEventArgs e)
    {
        if (PasswordRevealMode == PasswordRevealMode.Toggle)
        {
            SetCurrentValue(IsPasswordRevealedProperty, !IsPasswordRevealed);
        }
    }

    private static void OnPasswordChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        OnEditedTextChanged(a, e);
        if (a is PasswordBox box)
        {
            box.ForgetMask();
            box.SetCurrentValue(HasPasswordProperty, !string.IsNullOrEmpty(box.Password));
        }
    }

    private static void OnShownTextChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        (a as PasswordBox)?.RefreshDisplayedText();
}
