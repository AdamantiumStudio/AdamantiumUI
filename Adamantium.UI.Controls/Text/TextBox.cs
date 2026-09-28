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

    /// <summary>When true, Enter inserts a newline (multi-line editing). When false (default), Enter raises
    /// <see cref="EnterPressed"/> instead and the buffer stays single-line.</summary>
    public bool AcceptsReturn
    {
        get => GetValue<bool>(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    protected override bool AcceptsNewLines => AcceptsReturn;

    /// <summary>Raised when Enter is pressed while <see cref="AcceptsReturn"/> is off; a host can commit/submit here.</summary>
    public event KeyEventHandler EnterPressed;

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
