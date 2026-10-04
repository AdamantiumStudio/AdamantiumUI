using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>An editable text box over <see cref="TextBoxBase"/>. With <see cref="AcceptsReturn"/> off, Enter raises
/// <see cref="EnterPressed"/> instead of inserting a newline.</summary>
public class TextBox : TextBoxBase
{
    public static readonly AdamantiumProperty AcceptsReturnProperty = AdamantiumProperty.Register(nameof(AcceptsReturn),
        typeof(bool), typeof(TextBox), new PropertyMetadata(false));

    /// <summary>Whether the box offers a button that empties it in one press. The theme shows it while there is text
    /// and the box is not read-only.</summary>
    public static readonly AdamantiumProperty ShowsClearButtonProperty = AdamantiumProperty.Register(
        nameof(ShowsClearButton), typeof(bool), typeof(TextBox), new PropertyMetadata(false));

    /// <summary>What the clear button says when pointed at. The theme gives a general word; a box that clears something
    /// in particular, a search, says so.</summary>
    public static readonly AdamantiumProperty ClearButtonToolTipProperty = AdamantiumProperty.Register(
        nameof(ClearButtonToolTip), typeof(string), typeof(TextBox), new PropertyMetadata(null));

    /// <summary>Whether the box holds any text. The control keeps it; the theme reads it.</summary>
    public static readonly AdamantiumProperty HasTextProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasText),
        typeof(bool), typeof(TextBox), new PropertyMetadata(false));

    private ButtonBase _clearButton;

    static TextBox()
    {
        TextProperty.OverrideMetadata(typeof(TextBox), new PropertyMetadata(string.Empty, OnTextChanged));
    }

    /// <summary>When true, Enter inserts a newline (multi-line editing). When false (default), Enter raises
    /// <see cref="EnterPressed"/> instead and the buffer stays single-line.</summary>
    public bool AcceptsReturn
    {
        get => GetValue<bool>(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    public bool ShowsClearButton
    {
        get => GetValue<bool>(ShowsClearButtonProperty);
        set => SetValue(ShowsClearButtonProperty, value);
    }

    public string ClearButtonToolTip
    {
        get => GetValue<string>(ClearButtonToolTipProperty);
        set => SetValue(ClearButtonToolTipProperty, value);
    }

    public bool HasText => GetValue<bool>(HasTextProperty);

    protected override bool AcceptsNewLines => AcceptsReturn;

    /// <summary>Raised when Enter is pressed while <see cref="AcceptsReturn"/> is off; a host can commit/submit here.</summary>
    public event KeyEventHandler EnterPressed;

    /// <summary>Empties the box as one edit, which undo brings back. A read-only box is left alone.</summary>
    public void Clear()
    {
        SelectAll();
        ReplaceSelection(string.Empty);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _clearButton = GetTemplateChild("PART_ClearButton") as ButtonBase;
        if (_clearButton != null) _clearButton.Click += OnClearPressed;
    }

    public override void OnRemoveTemplate()
    {
        if (_clearButton != null) _clearButton.Click -= OnClearPressed;
        _clearButton = null;
        base.OnRemoveTemplate();
    }

    private void OnClearPressed(object sender, RoutedEventArgs e) => Clear();

    private static void OnTextChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is TextBox box) box.SetCurrentValue(HasTextProperty, !string.IsNullOrEmpty(box.Text));
    }

    protected override void OnUnhandledKey(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (AcceptsReturn) ReplaceSelection("\n");
            else EnterPressed?.Invoke(this, e);
            e.Handled = true;
        }
    }
}
