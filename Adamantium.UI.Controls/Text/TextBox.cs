using System.Globalization;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>An editable text box over <see cref="TextBoxBase"/>. With <see cref="AcceptsReturn"/> off, Enter raises
/// <see cref="EnterPressed"/> instead of inserting a newline.</summary>
public class TextBox : TextBoxBase
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(TextBox),
        new PropertyMetadata(string.Empty,
            PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly AdamantiumProperty AcceptsReturnProperty = AdamantiumProperty.Register(nameof(AcceptsReturn),
        typeof(bool), typeof(TextBox), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CharacterCasingProperty = AdamantiumProperty.Register(
        nameof(CharacterCasing), typeof(CharacterCasing), typeof(TextBox), new PropertyMetadata(CharacterCasing.Normal));

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
    private string _casingLanguage;
    private CultureInfo _casingCulture;

    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override AdamantiumProperty EditedTextProperty => TextProperty;

    /// <summary>When true, Enter inserts a newline (multi-line editing). When false (default), Enter raises
    /// <see cref="EnterPressed"/> instead and the buffer stays single-line.</summary>
    public bool AcceptsReturn
    {
        get => GetValue<bool>(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>The case given to what is typed or pasted, letter by letter as the box's <c>Language</c> maps it (else
    /// the application's language; a tag it does not know loses subtags from the right until it does). Text set from
    /// code or a binding keeps its own.</summary>
    public CharacterCasing CharacterCasing
    {
        get => GetValue<CharacterCasing>(CharacterCasingProperty);
        set => SetValue(CharacterCasingProperty, value);
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

    protected override string Entered(string text) => CharacterCasing switch
    {
        CharacterCasing.Upper => text.ToUpper(CasingCulture()),
        CharacterCasing.Lower => text.ToLower(CasingCulture()),
        _ => text,
    };

    private CultureInfo CasingCulture()
    {
        var language = Language?.Trim();
        if (!string.IsNullOrEmpty(language) && language != _casingLanguage)
        {
            _casingLanguage = language;
            _casingCulture = LookUp(language);
        }

        return (string.IsNullOrEmpty(language) ? null : _casingCulture)
               ?? LookUp(Languages.Current)
               ?? CultureInfo.InvariantCulture;
    }

    private static CultureInfo LookUp(string language)
    {
        for (var tag = language?.Trim() ?? string.Empty; tag.Length > 0;
             tag = tag.LastIndexOf('-') is > 0 and var dash ? tag[..dash] : string.Empty)
        {
            try
            {
                return Languages.CultureFor(tag);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return null;
    }

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
        OnEditedTextChanged(a, e);
        if (a is not TextBox box) return;
        box.SetCurrentValue(HasTextProperty, !string.IsNullOrEmpty(box.Text));
        AutomationEvents.RaisePropertyChanged(box, AutomationProperty.Value, e.OldValue, e.NewValue);
        if (AutomationEvents.IsListening && box.FindAutomationPeer() != null)
            AutomationEvents.Raise(box, AutomationEvent.TextChanged);
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

    protected override AutomationPeer OnCreateAutomationPeer() => new TextBoxAutomationPeer(this);
}
